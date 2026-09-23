import { useCallback, useEffect, useRef, useState } from "react";

export type MetronomeClick = Readonly<{
  time: number;
  accented: boolean;
  volume: number;
}>;

export function useAudioClock() {
  const contextRef = useRef<AudioContext | null>(null);
  const startedAtRef = useRef(0);
  const elapsedBeforeStartRef = useRef(0);
  const runningRef = useRef(false);
  const [isRunning, setIsRunning] = useState(false);

  const getElapsedSeconds = useCallback(() => {
    const context = contextRef.current;
    if (!context || !runningRef.current) return elapsedBeforeStartRef.current;
    return elapsedBeforeStartRef.current + context.currentTime - startedAtRef.current;
  }, []);

  const start = useCallback(() => {
    const context = contextRef.current ?? new AudioContext();
    contextRef.current = context;

    const begin = () => {
      startedAtRef.current = context.currentTime;
      runningRef.current = true;
      setIsRunning(true);
    };

    if (context.state === "suspended") {
      void context.resume().then(begin);
      return;
    }

    begin();
  }, []);

  const stop = useCallback(() => {
    elapsedBeforeStartRef.current = getElapsedSeconds();
    runningRef.current = false;
    setIsRunning(false);
  }, [getElapsedSeconds]);

  const reset = useCallback(() => {
    elapsedBeforeStartRef.current = 0;
    startedAtRef.current = contextRef.current?.currentTime ?? 0;
  }, []);

  const scheduleMetronomeClick = useCallback((click: MetronomeClick) => {
    const context = contextRef.current;
    if (!context || !runningRef.current || click.volume <= 0) return;

    const when = startedAtRef.current + click.time - elapsedBeforeStartRef.current;
    if (when < context.currentTime) return;

    const oscillator = context.createOscillator();
    const gain = context.createGain();
    oscillator.frequency.value = click.accented ? 1_200 : 880;
    gain.gain.setValueAtTime(0.0001, when);
    gain.gain.exponentialRampToValueAtTime(Math.max(click.volume, 0.0001) * 0.18, when + 0.003);
    gain.gain.exponentialRampToValueAtTime(0.0001, when + 0.045);
    oscillator.connect(gain).connect(context.destination);
    oscillator.start(when);
    oscillator.stop(when + 0.05);
  }, []);

  const subscribe = useCallback(
    (listener: (elapsed: number) => void) => {
      let frame: number | null = null;
      const update = () => {
        listener(getElapsedSeconds());
        frame = requestAnimationFrame(update);
      };

      frame = requestAnimationFrame(update);
      return () => {
        if (frame !== null) cancelAnimationFrame(frame);
      };
    },
    [getElapsedSeconds],
  );

  useEffect(
    () => () => {
      void contextRef.current?.close();
    },
    [],
  );

  return { getElapsedSeconds, isRunning, reset, scheduleMetronomeClick, start, stop, subscribe };
}
