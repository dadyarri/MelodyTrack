import { afterEach, describe, expect, it, vi } from "vitest";

import { canvasVideoBitrate, estimateVideoBytes, recordCanvasVideo } from "./recordCanvasVideo";

function mockRecording() {
  const videoTrack = { stop: vi.fn(), requestFrame: vi.fn() };
  const audioTrack = { stop: vi.fn() };
  class CanvasMock {
    captureStream() {
      return { getVideoTracks: () => [videoTrack], getTracks: () => [videoTrack] };
    }
  }
  const recorders: RecorderMock[] = [];
  const streams: Array<{ tracks: unknown[] }> = [];
  class RecorderMock {
    static isTypeSupported() {
      return true;
    }
    state = "inactive";
    ondataavailable: ((event: { data: Blob }) => void) | null = null;
    onstop: (() => void) | null = null;
    onerror: (() => void) | null = null;
    constructor() {
      recorders.push(this);
    }
    start() {
      this.state = "recording";
    }
    stop() {
      this.state = "inactive";
      queueMicrotask(() => {
        this.ondataavailable?.({ data: new Blob(["recorded video"]) });
        this.onstop?.();
      });
    }
  }
  vi.stubGlobal("HTMLCanvasElement", CanvasMock);
  vi.stubGlobal("MediaRecorder", RecorderMock);
  vi.stubGlobal(
    "MediaStream",
    class {
      constructor(public tracks: unknown[]) {
        streams.push(this);
      }
    },
  );
  return {
    streams,
    canvas: new CanvasMock() as unknown as HTMLCanvasElement,
    videoTrack,
    audioTrack,
    audioStream: { getAudioTracks: () => [audioTrack] } as unknown as MediaStream,
    recorders,
  };
}
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("canvas video recording", () => {
  it("estimates the encoded size from duration and bitrate", () => {
    expect(estimateVideoBytes(60)).toBe(37_500_000);
    expect(estimateVideoBytes(8, canvasVideoBitrate / 2)).toBe(2_500_000);
  });
  it("keeps a partial video when playback is stopped", async () => {
    const { canvas, audioStream, videoTrack, audioTrack, streams } = mockRecording();
    const controller = new AbortController();
    const recording = recordCanvasVideo({ canvas, audioStream, durationSeconds: 60, signal: controller.signal });

    controller.abort("finish");
    const result = await recording;

    expect(result.blob.size).toBeGreaterThan(0);
    expect(streams[0].tracks).toEqual([videoTrack, audioTrack]);
    expect(videoTrack.stop).toHaveBeenCalledOnce();
    expect(audioTrack.stop).not.toHaveBeenCalled();
  });
  it("paints before capturing and requests a populated first frame", async () => {
    const { canvas, videoTrack } = mockRecording();
    const drawFrame = vi.fn();
    const capture = vi.spyOn(canvas, "captureStream");
    const controller = new AbortController();
    const recording = recordCanvasVideo({ canvas, durationSeconds: 60, signal: controller.signal, drawFrame });

    expect(drawFrame.mock.invocationCallOrder[0]).toBeLessThan(capture.mock.invocationCallOrder[0]);
    expect(videoTrack.requestFrame).toHaveBeenCalled();
    controller.abort("finish");
    await recording;
  });
  it("discards video and releases the canvas stream when cancelled", async () => {
    const { canvas, videoTrack } = mockRecording();
    const controller = new AbortController();
    const recording = recordCanvasVideo({ canvas, durationSeconds: 60, signal: controller.signal });
    const rejected = expect(recording).rejects.toMatchObject({ name: "AbortError" });

    controller.abort();

    await rejected;
    expect(videoTrack.stop).toHaveBeenCalledOnce();
  });
  it("finishes and releases the canvas stream at the recording deadline", async () => {
    vi.useFakeTimers();
    const { canvas, videoTrack } = mockRecording();
    const recording = recordCanvasVideo({ canvas, durationSeconds: 2 });

    await vi.advanceTimersByTimeAsync(2_000);
    const result = await recording;

    expect(result.blob.size).toBeGreaterThan(0);
    expect(videoTrack.stop).toHaveBeenCalledOnce();
  });
  it("releases the canvas stream if recorder construction fails", async () => {
    const { canvas, videoTrack } = mockRecording();
    vi.stubGlobal(
      "MediaRecorder",
      Object.assign(
        function MockMediaRecorder() {
          throw new Error("unsupported codec");
        },
        { isTypeSupported: () => true },
      ),
    );

    await expect(recordCanvasVideo({ canvas, durationSeconds: 2 })).rejects.toThrow("Не удалось начать запись видео");
    expect(videoTrack.stop).toHaveBeenCalledOnce();
  });
  it("rejects recordings beyond the duration limit", async () => {
    const { canvas } = mockRecording();

    await expect(recordCanvasVideo({ canvas, durationSeconds: 181 })).rejects.toThrow("180 секунд");
  });
});
