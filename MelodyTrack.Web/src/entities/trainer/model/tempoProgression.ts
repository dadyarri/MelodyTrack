import type { TempoProgression } from "./practiceExercise";

export function shouldIncreaseTempo(progression: TempoProgression, completedBars: number, completedRepetitions: number) {
  const completedUnits = progression.unit === "bars" ? completedBars : completedRepetitions;
  return (
    progression.enabled &&
    completedUnits > 0 &&
    Number.isInteger(progression.every) &&
    progression.every > 0 &&
    completedUnits % progression.every === 0
  );
}
export function advanceExerciseTempo(
  progression: TempoProgression,
  bpm: number,
  completedBars: number,
  completedRepetitions: number,
  completesRepetition: boolean,
) {
  if (progression.unit === "repetitions" && !completesRepetition) return bpm;
  return shouldIncreaseTempo(progression, completedBars, completedRepetitions) ? Math.min(240, bpm + progression.increment) : bpm;
}
