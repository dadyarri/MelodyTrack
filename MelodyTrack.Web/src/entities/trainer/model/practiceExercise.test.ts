import { describe, expect, it } from "vitest";

import { createExerciseTimingCursor } from "./exerciseTiming";
import {
  changeStrummingGrid,
  createDefaultStrummingBar,
  createDefaultStrummingConfiguration,
  getCountInBeats,
  getCountInDurationSeconds,
  getStrummingCountLabels,
  getStrummingDurationSeconds,
  getStrummingPlaybackDurationSeconds,
  isCompleteStrummingBar,
  parseStrummingConfiguration,
  shouldIncreaseTempo,
} from "./practiceExercise";

describe("strumming configuration", () => {
  it("creates complete rows on the same fixed grid", () => {
    const configuration = createDefaultStrummingConfiguration();

    expect(configuration.bars[0].strokes.map((stroke) => stroke.action)).toEqual(["down", "up", "down", "up"]);
    expect(configuration.bars[0].handMotions).toEqual(["down", "up", "down", "up"]);
    expect(parseStrummingConfiguration(configuration)).toEqual(configuration);
  });
  it("calculates an iteration from tempo and configured bars", () => {
    expect(getStrummingDurationSeconds(createDefaultStrummingConfiguration())).toBe(3);
  });
  it("uses the same six beats for count-in and six-eight labels", () => {
    expect(getCountInBeats("6/8")).toBe(6);
    expect(getCountInDurationSeconds("6/8", 80)).toBe(2.25);
    expect(getStrummingCountLabels("6/8", 6)).toEqual(["1", "2", "3", "4", "5", "6"]);
    expect(getStrummingCountLabels("6/8", 12)).toEqual(["1", "и", "2", "и", "3", "и", "4", "и", "5", "и", "6", "и"]);
  });
  it("labels subdivisions of a four-four bar", () => {
    expect(getStrummingCountLabels("4/4", 8)).toEqual(["1", "и", "2", "и", "3", "и", "4", "и"]);
  });
  it("requires both rows to fill the grid with actions or rests", () => {
    const bar = { ...createDefaultStrummingConfiguration().bars[0], handMotions: ["down", "up"] as const };

    expect(isCompleteStrummingBar({ timeSignature: "4/4", ...bar })).toBe(false);
  });
  it("keeps later actions at the same time when an earlier action becomes a rest", () => {
    const bar = createDefaultStrummingBar("4/4");
    const cleared = {
      ...bar,
      strokes: bar.strokes.map((stroke, index) => (index === 1 ? { action: "rest" as const, accent: false } : stroke)),
    };

    const defaults = createDefaultStrummingConfiguration();
    const cursor = createExerciseTimingCursor(
      {
        bpm: 80,
        beatsPerBar: 4,
        quarterNotesPerBeat: 1,
        slotsPerBar: [cleared.strokes.length],
        repetitions: 1,
        loop: false,
        metronome: defaults.metronome,
        tempoProgression: defaults.tempoProgression,
      },
      0,
    );
    for (let index = 0; index < 4; index += 1) cursor.next();
    expect(Array.from({ length: 4 }, () => cursor.next()?.position.time)).toEqual([3, 3.75, 4.5, 5.25]);
    expect(cleared.strokes[2]).toEqual(bar.strokes[2]);
  });
  it("preserves action positions when adding subdivisions", () => {
    const bar = createDefaultStrummingBar("4/4");

    const finer = changeStrummingGrid(bar, "4/4", "eighth");

    expect(finer?.strokes.map((stroke) => stroke.action)).toEqual(["down", "rest", "up", "rest", "down", "rest", "up", "rest"]);
    expect(finer?.handMotions).toEqual(["down", "rest", "up", "rest", "down", "rest", "up", "rest"]);
  });
  it("requires confirmation before a coarser grid discards actions", () => {
    expect(changeStrummingGrid(createDefaultStrummingBar("4/4", "eighth"), "4/4", "quarter")).toBeNull();
  });
  it("restores the original grid when extra slots contain only rests", () => {
    const bar = createDefaultStrummingBar("4/4");
    const finer = changeStrummingGrid(bar, "4/4", "eighth");
    if (!finer) throw new Error("Expected a finer grid");

    expect(changeStrummingGrid(finer, "4/4", "quarter")).toEqual(bar);
  });
  it("increases tempo at configured bar or repetition boundaries", () => {
    const progression = { enabled: true, increment: 5, every: 2, unit: "bars" as const };

    expect(shouldIncreaseTempo(progression, 1, 0)).toBe(false);
    expect(shouldIncreaseTempo(progression, 2, 0)).toBe(true);
    expect(shouldIncreaseTempo({ ...progression, unit: "repetitions" }, 2, 2)).toBe(true);
  });
  it("includes tempo increases in the default recording duration", () => {
    const configuration = {
      ...createDefaultStrummingConfiguration(),
      bpm: 60,
      repetitions: 2,
      tempoProgression: { enabled: true, every: 1, increment: 60, unit: "repetitions" as const },
    };

    expect(getStrummingPlaybackDurationSeconds(configuration)).toBe(6);
  });
  it.each([0, -1, 29, 241, Infinity, NaN])("rejects an unsafe tempo of %s", (bpm) => {
    expect(parseStrummingConfiguration({ ...createDefaultStrummingConfiguration(), bpm })).toBeNull();
  });
  it.each([0, 1.5, 101])("rejects an invalid repeat count of %s", (repetitions) => {
    expect(parseStrummingConfiguration({ ...createDefaultStrummingConfiguration(), repetitions })).toBeNull();
  });
  it("rejects unknown configuration versions and missing fields", () => {
    expect(parseStrummingConfiguration({ ...createDefaultStrummingConfiguration(), schemaVersion: 2 })).toBeNull();
    expect(parseStrummingConfiguration({})).toBeNull();
    expect(parseStrummingConfiguration(null)).toBeNull();
  });
  it("rejects an unsupported grid in six-eight", () => {
    expect(
      parseStrummingConfiguration({ ...createDefaultStrummingConfiguration("6/8"), bars: [createDefaultStrummingBar("6/8", "quarter")] }),
    ).toBeNull();
  });
});
