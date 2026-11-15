import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError, applicationApi } from "./http";
import {
  emitDataChange,
  openDataChanges,
  readDataChanges,
  startDataChanges,
  subscribeDataChanges,
} from "./live-data";

const encoder = new TextEncoder();
function stream(...chunks: string[]) {
  return new Response(
    new ReadableStream<Uint8Array>({
      start(controller) {
        for (const chunk of chunks) controller.enqueue(encoder.encode(chunk));
        controller.close();
      },
    }),
    { headers: { "Content-Type": "text/event-stream" } },
  );
}

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("live data transport", () => {
  it("reads fragmented SSE frames and ignores comments, unknown events and incomplete frames", async () => {
    const changed = vi.fn();

    await readDataChanges(
      stream(
        ": heartbeat\r\neve",
        "nt: invalidate\r",
        "\ndata: {}\r\n\r",
        "\n",
        "event: unknown\ndata: {}\n\nevent: invalidate\ndata: {}\n\n",
        "event: invalidate\ndata: unfinished",
      ),
      new AbortController().signal,
      changed,
    );

    expect(changed).toHaveBeenCalledTimes(2);
  });

  it("uses the generated route and bearer middleware without buffering the response", async () => {
    let closed = false;
    const body = new ReadableStream<Uint8Array>({
      cancel() {
        closed = true;
      },
    });
    const fetch = vi.fn().mockResolvedValue(
      new Response(body, {
        headers: { "Content-Type": "text/event-stream; charset=utf-8" },
      }),
    );
    vi.stubGlobal("fetch", fetch);

    const result = await openDataChanges(
      applicationApi("memory-only-token"),
      new AbortController().signal,
    );

    expect(result.body).toBe(body);
    expect(closed).toBe(false);
    const [url, init] = fetch.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`${window.location.origin}/api/changes/events`);
    expect(url).not.toContain("token");
    expect(new Headers(init.headers).get("Authorization")).toBe("Bearer memory-only-token");
    expect(new Headers(init.headers).get("Accept")).toContain("text/event-stream");
    expect(init.credentials).toBe("include");
    await body.cancel();
  });

  it("reconnects after EOF and synchronizes missed changes from the new connection", async () => {
    vi.useFakeTimers();
    const change = vi.fn();
    const open = vi.fn().mockResolvedValue(stream("event: invalidate\ndata: {}\n\n"));
    const stop = startDataChanges(open, change);

    await vi.advanceTimersByTimeAsync(0);
    expect(change).toHaveBeenCalledOnce();
    open.mockResolvedValue(stream("event: invalidate\ndata: {}\n\n"));
    await vi.advanceTimersByTimeAsync(1000);

    expect(open).toHaveBeenCalledTimes(2);
    expect(change).toHaveBeenCalledTimes(2);
    stop();
    await vi.advanceTimersByTimeAsync(60_000);
    expect(open).toHaveBeenCalledTimes(2);
  });

  it("backs off failed connections and cancels the active stream on cleanup", async () => {
    vi.useFakeTimers();
    const cancel = vi.fn();
    const open = vi
      .fn()
      .mockRejectedValueOnce(new ApiError("offline"))
      .mockRejectedValueOnce(new ApiError("offline"))
      .mockResolvedValue(
        new Response(new ReadableStream({ cancel }), {
          headers: { "Content-Type": "text/event-stream" },
        }),
      );
    const stop = startDataChanges(open);

    await vi.advanceTimersByTimeAsync(1000);
    expect(open).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(1999);
    expect(open).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(1);
    expect(open).toHaveBeenCalledTimes(3);
    stop();
    await vi.advanceTimersByTimeAsync(0);

    expect(cancel).toHaveBeenCalledOnce();
    expect(vi.getTimerCount()).toBe(0);
  });

  it("backs off repeated disconnects even when each connection sends its initial invalidation", async () => {
    vi.useFakeTimers();
    const open = vi.fn().mockImplementation(async () => stream("event: invalidate\ndata: {}\n\n"));
    const stop = startDataChanges(open, vi.fn());

    await vi.advanceTimersByTimeAsync(1000);
    expect(open).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(1999);
    expect(open).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(1);
    expect(open).toHaveBeenCalledTimes(3);
    await vi.advanceTimersByTimeAsync(3999);
    expect(open).toHaveBeenCalledTimes(3);
    await vi.advanceTimersByTimeAsync(1);
    expect(open).toHaveBeenCalledTimes(4);
    stop();
  });

  it("retries a silent broken stream after its heartbeat deadline", async () => {
    vi.useFakeTimers();
    const cancel = vi.fn();
    const open = vi.fn().mockImplementation(
      async () =>
        new Response(new ReadableStream({ cancel }), {
          headers: { "Content-Type": "text/event-stream" },
        }),
    );
    const stop = startDataChanges(open);

    await vi.advanceTimersByTimeAsync(46_000);

    expect(cancel).toHaveBeenCalledOnce();
    expect(open).toHaveBeenCalledTimes(2);
    stop();
  });

  it("does not retry revoked access on a timer", async () => {
    vi.useFakeTimers();
    const open = vi.fn().mockRejectedValue(new ApiError("ended", 401));
    const stop = startDataChanges(open);

    await vi.advanceTimersByTimeAsync(120_000);

    expect(open).toHaveBeenCalledOnce();
    stop();
  });

  it("keeps a background stream and accepts heartbeats after returning to the tab", async () => {
    vi.useFakeTimers();
    let visibility = "visible";
    vi.spyOn(document, "visibilityState", "get").mockImplementation(
      () => visibility as DocumentVisibilityState,
    );
    let writer!: ReadableStreamDefaultController<Uint8Array>;
    const cancel = vi.fn();
    const open = vi.fn().mockResolvedValue(
      new Response(
        new ReadableStream<Uint8Array>({
          start(controller) {
            writer = controller;
          },
          cancel,
        }),
      ),
    );
    const changed = vi.fn();
    const stop = startDataChanges(open, changed);
    await vi.advanceTimersByTimeAsync(0);

    visibility = "hidden";
    document.dispatchEvent(new Event("visibilitychange"));
    await vi.advanceTimersByTimeAsync(120_000);
    visibility = "visible";
    document.dispatchEvent(new Event("visibilitychange"));
    window.dispatchEvent(new Event("focus"));
    await vi.advanceTimersByTimeAsync(30_000);
    writer.enqueue(encoder.encode(": keep-alive\n\n"));
    await vi.advanceTimersByTimeAsync(30_000);
    writer.enqueue(encoder.encode("event: invalidate\ndata: 1\n\n"));
    await vi.advanceTimersByTimeAsync(0);

    expect(open).toHaveBeenCalledOnce();
    expect(cancel).not.toHaveBeenCalled();
    expect(changed).toHaveBeenCalledOnce();
    stop();
    await vi.advanceTimersByTimeAsync(0);
    expect(vi.getTimerCount()).toBe(0);
  });

  it("waits for a fresh heartbeat deadline after window focus and recovers a silent stream", async () => {
    vi.useFakeTimers();
    const cancel = vi.fn();
    const open = vi
      .fn()
      .mockImplementation(async () => new Response(new ReadableStream({ cancel })));
    const stop = startDataChanges(open);
    await vi.advanceTimersByTimeAsync(0);

    window.dispatchEvent(new Event("blur"));
    await vi.advanceTimersByTimeAsync(120_000);
    window.dispatchEvent(new Event("focus"));
    await vi.advanceTimersByTimeAsync(44_999);

    expect(open).toHaveBeenCalledOnce();
    expect(cancel).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1001);
    expect(open).toHaveBeenCalledTimes(2);
    expect(cancel).toHaveBeenCalledOnce();
    stop();
    await vi.advanceTimersByTimeAsync(0);
    window.dispatchEvent(new Event("focus"));
    expect(vi.getTimerCount()).toBe(0);
  });

  it("waits while offline and reconnects immediately when connectivity returns", async () => {
    vi.useFakeTimers();
    const navigator = { onLine: false };
    vi.stubGlobal("navigator", navigator);
    const open = vi.fn().mockResolvedValue(stream("event: invalidate\ndata: {}\n\n"));
    const change = vi.fn();
    const stop = startDataChanges(open, change);

    await vi.advanceTimersByTimeAsync(30_000);
    expect(open).not.toHaveBeenCalled();
    navigator.onLine = true;
    window.dispatchEvent(new Event("online"));
    await vi.advanceTimersByTimeAsync(0);

    expect(open).toHaveBeenCalledOnce();
    expect(change).toHaveBeenCalledOnce();
    stop();
  });

  it("does not retain unsubscribed screens", () => {
    const listener = vi.fn();
    const stop = subscribeDataChanges(listener);
    emitDataChange();
    stop();
    emitDataChange();
    expect(listener).toHaveBeenCalledOnce();
  });

  it("does not publish an aborted connection or retain its stream reader", async () => {
    const controller = new AbortController();
    const changed = vi.fn();
    const cancel = vi.fn();
    const response = new Response(new ReadableStream<Uint8Array>({ cancel }));
    const reading = readDataChanges(response, controller.signal, changed);

    controller.abort();
    await reading;

    expect(changed).not.toHaveBeenCalled();
    expect(cancel).toHaveBeenCalledOnce();
    expect(response.body?.locked).toBe(false);
  });
});
