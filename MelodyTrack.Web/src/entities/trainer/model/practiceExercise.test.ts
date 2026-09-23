import { describe, expect, it } from "vitest";

import {
  createDefaultStrummingConfiguration,
  fixedCountInDurationSeconds,
  getActiveStrummingEventIndex,
  getEmptyStrummingGridSpans,
  getStrummingCountLabels,
  getStrummingDurationSeconds,
  isCompleteStrummingBar,
  shouldIncreaseTempo,
} from "./practiceExercise";

describe("strumming configuration", () => {
  it("creates a complete four-four beginner pattern", () => {
    const configuration = createDefaultStrummingConfiguration();

    expect(configuration.bars[0].events.map((event) => event.action)).toEqual(["down", "up", "down", "up"]);
    expect(isCompleteStrummingBar({ timeSignature: configuration.timeSignature, ...configuration.bars[0] })).toBe(true);
  });

  it("calculates an iteration from tempo and configured bars", () => {
    expect(getStrummingDurationSeconds(createDefaultStrummingConfiguration())).toBe(3);
  });

  it("uses a fixed five-second count-in", () => {
    expect(fixedCountInDurationSeconds).toBe(5);
  });

  it("selects uneven events from their musical position without accumulating elapsed-time error", () => {
    const configuration = {
      ...createDefaultStrummingConfiguration(),
      bars: [
        {
          events: [
            { rhythmicValue: "eighth" as const, handMotion: "down" as const, action: "down" as const, accent: false },
            { rhythmicValue: "eighth" as const, handMotion: "up" as const, action: "up" as const, accent: false },
            { rhythmicValue: "quarter" as const, handMotion: "down" as const, action: "down" as const, accent: false },
            { rhythmicValue: "quarter" as const, handMotion: "up" as const, action: "up" as const, accent: false },
            { rhythmicValue: "quarter" as const, handMotion: "down" as const, action: "down" as const, accent: false },
          ],
        },
      ],
    };

    expect(getActiveStrummingEventIndex(configuration, 0.375)).toBe(1);
    expect(getActiveStrummingEventIndex(configuration, 0.75)).toBe(2);
    expect(getActiveStrummingEventIndex(configuration, 1.5)).toBe(3);
    expect(getActiveStrummingEventIndex(configuration, 2.25)).toBe(4);
  });

  it("increases tempo at configured bar or repetition boundaries", () => {
    const progression = { enabled: true, increment: 5, every: 2, unit: "bars" as const };

    expect(shouldIncreaseTempo(progression, 1, 0)).toBe(false);
    expect(shouldIncreaseTempo(progression, 2, 0)).toBe(true);
    expect(shouldIncreaseTempo({ ...progression, unit: "repetitions" }, 2, 1)).toBe(false);
    expect(shouldIncreaseTempo({ ...progression, unit: "repetitions" }, 2, 2)).toBe(true);
  });

  it("labels quarter, eighth, and sixteenth strums by their musical count", () => {
    expect(getStrummingCountLabels(createDefaultStrummingConfiguration().bars[0].events)).toEqual(["1", "2", "3", "4"]);
    expect(getStrummingCountLabels(Array.from({ length: 8 }, () => ({ rhythmicValue: "eighth" as const })))).toEqual([
      "1",
      "и",
      "2",
      "и",
      "3",
      "и",
      "4",
      "и",
    ]);
    expect(getStrummingCountLabels(Array.from({ length: 16 }, () => ({ rhythmicValue: "sixteenth" as const })))).toEqual([
      "1",
      "та",
      "и",
      "та",
      "2",
      "та",
      "и",
      "та",
      "3",
      "та",
      "и",
      "та",
      "4",
      "та",
      "и",
      "та",
    ]);
  });

  it("keeps mixed-duration labels and empty grid slots on their musical positions", () => {
    const events = [
      { rhythmicValue: "quarter" as const },
      { rhythmicValue: "eighth" as const },
      { rhythmicValue: "eighth" as const },
      { rhythmicValue: "sixteenth" as const },
    ];

    expect(getStrummingCountLabels(events)).toEqual(["1", "2", "и", "3"]);
    const configuration = {
      ...createDefaultStrummingConfiguration(),
      events: events.map((event, index) => ({ ...createDefaultStrummingConfiguration().bars[0].events[index], ...event })),
    };

    expect(getEmptyStrummingGridSpans(configuration)).toBe(21);
  });
});
