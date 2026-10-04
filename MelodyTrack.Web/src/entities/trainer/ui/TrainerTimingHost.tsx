import { createContext, type ReactNode, useCallback, useContext, useEffect, useId, useMemo, useRef, useState } from "react";

import { useAudioClock } from "@/shared/lib";
import { createExerciseTimingCursor, type ExerciseTimingConfiguration, type ExerciseTimingPosition } from "../model/exerciseTiming";

type AudioClock = ReturnType<typeof useAudioClock>;
type TimingOwner = { id: string; onRelease: () => void; onPosition: (position: ExerciseTimingPosition) => void };
type TimingAuthority = {
  clock: AudioClock;
  owner: string | null;
  position: ExerciseTimingPosition | null;
  start: (owner: TimingOwner, configuration: ExerciseTimingConfiguration) => Promise<boolean>;
  release: (id: string) => void;
};
const TimingContext = createContext<TimingAuthority | null>(null);

/** Nested hosts reuse the page's authority instead of allocating another clock. */
export function TrainerTimingHost({ children }: { children: ReactNode }) {
  const existing = useContext(TimingContext);
  return existing ? children : <TimingProvider>{children}</TimingProvider>;
}
function TimingProvider({ children }: { children: ReactNode }) {
  const clock = useAudioClock();
  const ownerRef = useRef<TimingOwner | null>(null);
  const cancelSchedulerRef = useRef<(() => void) | null>(null);
  const [owner, setOwner] = useState<string | null>(null);
  const [position, setPosition] = useState<ExerciseTimingPosition | null>(null);
  const release = useCallback((id: string) => {
    if (ownerRef.current?.id !== id) return;
    ownerRef.current = null;
    cancelSchedulerRef.current?.();
    cancelSchedulerRef.current = null;
    clock.stop();
    setOwner(null);
  }, [clock.stop]);
  const start = useCallback(async (next: TimingOwner, configuration: ExerciseTimingConfiguration) => {
    // Validate before releasing a working exercise or allocating audio resources.
    createExerciseTimingCursor(configuration);
    const previous = ownerRef.current;
    if (previous) { release(previous.id); previous.onRelease(); }
    ownerRef.current = next;
    setOwner(next.id);
    setPosition(null);
    clock.reset();
    let started: boolean;
    try { started = await clock.start(); }
    catch (error) { if (ownerRef.current === next) release(next.id); throw error; }
    if (!started || ownerRef.current !== next) { if (ownerRef.current === next) release(next.id); return false; }
    const cursor = createExerciseTimingCursor(configuration, clock.getElapsedSeconds() + 0.06);
    const scheduled: ExerciseTimingPosition[] = [];
    let timer: ReturnType<typeof setTimeout> | undefined;
    let frame: number | undefined;
    let cancelled = false;
    const schedule = () => {
      if (cancelled) return;
      const until = clock.getElapsedSeconds() + 0.12;
      while (!cursor.complete && cursor.nextTime < until) {
        const step = cursor.next();
        if (!step) break;
        scheduled.push(step.position);
        if (configuration.metronome.enabled && step.click) clock.scheduleMetronomeClick({ time: step.position.time, accented: step.accented, volume: configuration.metronome.volume });
      }
      if (!cursor.complete) timer = setTimeout(schedule, 25);
    };
    const draw = () => {
      if (cancelled) return;
      const elapsed = clock.getElapsedSeconds();
      let current: ExerciseTimingPosition | undefined;
      while (scheduled.length && scheduled[0].time <= elapsed) current = scheduled.shift();
      if (current) { next.onPosition(current); setPosition(current); }
      if (cursor.complete && elapsed >= cursor.nextTime) { release(next.id); next.onRelease(); return; }
      frame = requestAnimationFrame(draw);
    };
    cancelSchedulerRef.current = () => {
      cancelled = true;
      if (timer !== undefined) clearTimeout(timer);
      if (frame !== undefined) cancelAnimationFrame(frame);
    };
    schedule();
    frame = requestAnimationFrame(draw);
    return true;
  }, [clock.reset, clock.start, clock.getElapsedSeconds, clock.scheduleMetronomeClick, release]);
  useEffect(() => () => { cancelSchedulerRef.current?.(); ownerRef.current = null; }, []);
  const authority = useMemo(() => ({ clock, owner, position, start, release }), [clock, owner, position, start, release]);
  return <TimingContext.Provider value={authority}>{children}</TimingContext.Provider>;
}
export function useTrainerTiming(onRelease: () => void, onPosition?: (position: ExerciseTimingPosition) => void) {
  const authority = useContext(TimingContext);
  if (!authority) throw new Error("Trainer timing requires a host");
  const id = useId();
  const { clock, owner, position, start: startSession, release } = authority;
  const onReleaseRef = useRef(onRelease);
  const onPositionRef = useRef(onPosition);
  useEffect(() => { onReleaseRef.current = onRelease; onPositionRef.current = onPosition; }, [onRelease, onPosition]);
  useEffect(() => () => release(id), [id, release]);
  const start = useCallback((configuration: ExerciseTimingConfiguration) => startSession({ id, onRelease: () => onReleaseRef.current(), onPosition: position => onPositionRef.current?.(position) }, configuration), [startSession, id]);
  const stop = useCallback(() => release(id), [id, release]);
  return { getAudioStream: clock.getAudioStream, isRunning: owner === id && clock.isRunning, position: owner === id ? position : null, start, stop };
}
