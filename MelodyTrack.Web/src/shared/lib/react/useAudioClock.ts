import { useCallback, useEffect, useRef, useState } from "react";

export type MetronomeClick = Readonly<{ time: number; accented: boolean; volume: number }>;

export function useAudioClock() {
  const contextRef = useRef<AudioContext | null>(null);
  const recordingDestinationRef = useRef<MediaStreamAudioDestinationNode | null>(null);
  const sourcesRef = useRef(new Set<OscillatorNode>());
  const generationRef = useRef(0);
  const startedAtRef = useRef(0);
  const elapsedBeforeStartRef = useRef(0);
  const runningRef = useRef(false);
  const [isRunning, setIsRunning] = useState(false);
  const getElapsedSeconds = useCallback(() => {
    const context = contextRef.current;
    return !context || !runningRef.current
      ? elapsedBeforeStartRef.current
      : elapsedBeforeStartRef.current + context.currentTime - startedAtRef.current;
  }, []);
  const ensureContext = useCallback(() => {
    const context = contextRef.current ?? new AudioContext();
    contextRef.current = context;
    recordingDestinationRef.current ??= context.createMediaStreamDestination();
    return context;
  }, []);
  const getAudioStream = useCallback(() => {
    ensureContext();
    return recordingDestinationRef.current?.stream ?? null;
  }, [ensureContext]);
  const cancelSources = useCallback(() => {
    for (const oscillator of sourcesRef.current) {
      oscillator.stop();
      oscillator.disconnect();
    }
    sourcesRef.current.clear();
  }, []);
  const start = useCallback(async () => {
    const context = ensureContext();
    const generation = ++generationRef.current;
    // resume is invoked synchronously from the user's click, before awaiting it.
    if (context.state === "suspended") await context.resume();
    if (generation !== generationRef.current || context.state !== "running") return false;
    startedAtRef.current = context.currentTime;
    runningRef.current = true;
    setIsRunning(true);
    return true;
  }, [ensureContext]);
  const stop = useCallback(() => {
    generationRef.current += 1;
    elapsedBeforeStartRef.current = getElapsedSeconds();
    runningRef.current = false;
    cancelSources();
    setIsRunning(false);
  }, [cancelSources, getElapsedSeconds]);
  const reset = useCallback(() => {
    elapsedBeforeStartRef.current = 0;
    startedAtRef.current = contextRef.current?.currentTime ?? 0;
  }, []);
  const scheduleMetronomeClick = useCallback((click: MetronomeClick) => {
    const context = contextRef.current;
    if (!context || !runningRef.current || click.volume <= 0) return;
    const when = startedAtRef.current + click.time - elapsedBeforeStartRef.current;
    if (!Number.isFinite(when) || when < context.currentTime) return;
    const oscillator = context.createOscillator();
    const gain = context.createGain();
    oscillator.frequency.value = click.accented ? 1_200 : 880;
    gain.gain.setValueAtTime(0.0001, when);
    gain.gain.exponentialRampToValueAtTime(Math.max(click.volume, 0.0001) * 0.18, when + 0.003);
    gain.gain.exponentialRampToValueAtTime(0.0001, when + 0.045);
    oscillator.connect(gain);
    gain.connect(context.destination);
    if (recordingDestinationRef.current) gain.connect(recordingDestinationRef.current);
    sourcesRef.current.add(oscillator);
    oscillator.onended = () => {
      sourcesRef.current.delete(oscillator);
      oscillator.disconnect();
      gain.disconnect();
    };
    oscillator.start(when);
    oscillator.stop(when + 0.05);
  }, []);
  useEffect(
    () => () => {
      generationRef.current += 1;
      runningRef.current = false;
      cancelSources();
      const context = contextRef.current;
      contextRef.current = null;
      recordingDestinationRef.current = null;
      void context?.close().catch(() => {});
    },
    [cancelSources],
  );
  return { getAudioStream, getElapsedSeconds, isRunning, reset, scheduleMetronomeClick, start, stop };
}
