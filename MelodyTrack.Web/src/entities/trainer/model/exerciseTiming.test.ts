import { describe, expect, it } from "vitest";

import { createExerciseTimingCursor, type ExerciseTimingConfiguration } from "./exerciseTiming";

const configuration: ExerciseTimingConfiguration = {
  bpm: 60,
  beatsPerBar: 4,
  quarterNotesPerBeat: 1,
  slotsPerBar: [4, 8],
  repetitions: 2,
  loop: false,
  metronome: { enabled: true, volume: 0.6 },
  tempoProgression: { enabled: false, increment: 60, every: 1, unit: "bars" },
};
function stepsFor(value: ExerciseTimingConfiguration) {
  const cursor = createExerciseTimingCursor(value, 0);
  const steps = [];
  while (!cursor.complete) {
    const step = cursor.next();
    if (step) steps.push(step);
  }
  return { steps, end: cursor.nextTime };
}
describe("shared exercise timing", () => {
  it("counts in before advancing through every bar and repetition", () => {
    const { steps, end } = stepsFor(configuration);

    expect(steps.slice(0, 4).map((step) => step.position.countIn)).toEqual([1, 2, 3, 4]);
    expect(
      steps.filter((step) => step.position.slot === 0).map((step) => [step.position.bar, step.position.repetition, step.position.time]),
    ).toEqual([
      [0, 1, 4],
      [1, 1, 8],
      [0, 2, 12],
      [1, 2, 16],
    ]);
    expect(end).toBe(20);
  });
  it("changes tempo after completed bars while keeping the current bar at its original tempo", () => {
    const { steps } = stepsFor({ ...configuration, tempoProgression: { ...configuration.tempoProgression, enabled: true } });

    expect(steps.filter((step) => step.position.slot === 0).map((step) => step.position.bpm)).toEqual([60, 120, 180, 240]);
    expect(
      steps.filter((step) => step.position.bar === 0 && step.position.repetition === 1).every((step) => step.position.bpm === 60),
    ).toBe(true);
  });
  it("changes tempo only after a complete repetition when configured to do so", () => {
    const { steps } = stepsFor({
      ...configuration,
      tempoProgression: { ...configuration.tempoProgression, enabled: true, unit: "repetitions" },
    });

    expect(steps.filter((step) => step.position.slot === 0).map((step) => step.position.bpm)).toEqual([60, 60, 120, 120]);
  });
  it("respects the configured progression interval", () => {
    const { steps } = stepsFor({ ...configuration, tempoProgression: { ...configuration.tempoProgression, enabled: true, every: 2 } });

    expect(steps.filter((step) => step.position.slot === 0).map((step) => step.position.bpm)).toEqual([60, 60, 120, 120]);
  });
  it("clicks on beats rather than on every subdivision and accents each first beat", () => {
    const { steps } = stepsFor(configuration);
    const secondBar = steps.filter((step) => step.position.bar === 1 && step.position.repetition === 1);

    expect(secondBar.filter((step) => step.click).map((step) => step.position.slot)).toEqual([0, 2, 4, 6]);
    expect(secondBar.filter((step) => step.accented).map((step) => step.position.slot)).toEqual([0]);
  });
  it("keeps six-eight count-in and exercise clicks on the same eighth-note timing", () => {
    const { steps, end } = stepsFor({ ...configuration, beatsPerBar: 6, quarterNotesPerBeat: 0.5, slotsPerBar: [6], repetitions: 1 });

    expect(steps.slice(0, 6).map((step) => step.position.time)).toEqual([0, 0.5, 1, 1.5, 2, 2.5]);
    expect(steps.filter((step) => step.position.bar === 0).map((step) => step.position.time)).toEqual([3, 3.5, 4, 4.5, 5, 5.5]);
    expect(end).toBe(6);
  });
  it("rejects a grid that cannot place its metronome beats", () => {
    expect(() => createExerciseTimingCursor({ ...configuration, slotsPerBar: [5] })).toThrow("Некорректные параметры ритма");
  });
  it("rejects non-finite tempo before starting a scheduler", () => {
    expect(() => createExerciseTimingCursor({ ...configuration, bpm: Infinity })).toThrow("Некорректные параметры ритма");
  });
  it("continues looping beyond the finite repetition setting", () => {
    const cursor = createExerciseTimingCursor({ ...configuration, slotsPerBar: [4], repetitions: 1, loop: true });
    for (let index = 0; index < 12; index += 1) cursor.next();

    expect(cursor.complete).toBe(false);
    expect(cursor.next()?.position.repetition).toBe(3);
  });
});
