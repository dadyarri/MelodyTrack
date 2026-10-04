import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useAudioClock } from "./useAudioClock";

function mockAudioContext(state: "running" | "suspended" = "running") {
  const oscillators: Array<{ stop: ReturnType<typeof vi.fn>; disconnect: ReturnType<typeof vi.fn>; start: ReturnType<typeof vi.fn> }> = [];
  const resume = vi.fn<() => Promise<void>>();
  const recordingDestination = { stream: {} };
  const gains: Array<{ connect: ReturnType<typeof vi.fn> }> = [];
  const context = {
    state,
    currentTime: 0,
    destination: {},
    resume,
    close: vi.fn(async () => {}),
    createMediaStreamDestination: () => recordingDestination,
    createGain: () => {
      const gain = { gain: { setValueAtTime: vi.fn(), exponentialRampToValueAtTime: vi.fn() }, connect: vi.fn(), disconnect: vi.fn() };
      gains.push(gain);
      return gain;
    },
    createOscillator: () => {
      const oscillator = { frequency: { value: 0 }, connect: vi.fn(), disconnect: vi.fn(), start: vi.fn(), stop: vi.fn(), onended: null };
      oscillators.push(oscillator);
      return oscillator;
    },
  };
  resume.mockImplementation(async () => { context.state = "running"; });
  vi.stubGlobal("AudioContext", class { constructor() { return context; } });
  return { context, oscillators, resume, gains, recordingDestination };
}
afterEach(() => vi.unstubAllGlobals());

describe("audio clock", () => {
  it("cancels scheduled sounds when stopping playback", async () => {
    const { oscillators } = mockAudioContext();
    const { result } = renderHook(useAudioClock);
    await act(async () => { await result.current.start(); });
    result.current.scheduleMetronomeClick({ time: 2, accented: false, volume: 0.6 });

    act(() => result.current.stop());

    expect(oscillators[0].stop).toHaveBeenLastCalledWith();
    expect(oscillators[0].disconnect).toHaveBeenCalled();
    expect(result.current.isRunning).toBe(false);
  });
  it("routes metronome clicks to both speakers and the recording audio stream", async () => {
    const { context, gains, recordingDestination } = mockAudioContext();
    const { result } = renderHook(useAudioClock);
    const audioStream = result.current.getAudioStream();
    await act(async () => { await result.current.start(); });

    result.current.scheduleMetronomeClick({ time: 1, accented: true, volume: 0.6 });

    expect(audioStream).toBe(recordingDestination.stream);
    expect(gains[0].connect).toHaveBeenCalledWith(context.destination);
    expect(gains[0].connect).toHaveBeenCalledWith(recordingDestination);
  });
  it("does not restart after stop while audio permission is pending", async () => {
    const { resume, context } = mockAudioContext("suspended");
    let resolveResume: (() => void) | undefined;
    resume.mockImplementation(() => new Promise<void>(resolve => { resolveResume = () => { context.state = "running"; resolve(); }; }));
    const { result } = renderHook(useAudioClock);
    let starting: Promise<boolean> | undefined;
    act(() => { starting = result.current.start(); });

    act(() => result.current.stop());
    await act(async () => { resolveResume?.(); await starting; });

    expect(await starting).toBe(false);
    expect(result.current.isRunning).toBe(false);
  });
  it("closes audio and cancels future clicks on unmount", async () => {
    const { context, oscillators } = mockAudioContext();
    const { result, unmount } = renderHook(useAudioClock);
    await act(async () => { await result.current.start(); });
    result.current.scheduleMetronomeClick({ time: 3, accented: true, volume: 0.6 });

    unmount();

    expect(oscillators[0].stop).toHaveBeenLastCalledWith();
    expect(context.close).toHaveBeenCalledOnce();
  });
});
