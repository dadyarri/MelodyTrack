import { useCallback, useEffect, useRef, useState } from "react";

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

  return { getElapsedSeconds, isRunning, reset, start, stop, subscribe };
}
