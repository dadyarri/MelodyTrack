import { describe, expect, it } from "vitest";

import {
  createDefaultStrummingConfiguration,
  fixedCountInDurationSeconds,
  getActiveStrummingEventIndex,
  getStrummingDurationSeconds,
  isCompleteStrummingBar,
} from "./practiceExercise";

describe("strumming configuration", () => {
  it("creates a complete four-four beginner pattern", () => {
    const configuration = createDefaultStrummingConfiguration();

    expect(configuration.events.map((event) => event.action)).toEqual(["down", "up", "down", "up"]);
    expect(isCompleteStrummingBar(configuration)).toBe(true);
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
      events: [
        { rhythmicValue: "eighth" as const, handMotion: "down" as const, action: "down" as const, accent: false },
        { rhythmicValue: "eighth" as const, handMotion: "up" as const, action: "up" as const, accent: false },
        { rhythmicValue: "quarter" as const, handMotion: "down" as const, action: "down" as const, accent: false },
        { rhythmicValue: "quarter" as const, handMotion: "up" as const, action: "up" as const, accent: false },
        { rhythmicValue: "quarter" as const, handMotion: "down" as const, action: "down" as const, accent: false },
      ],
    };

    expect(getActiveStrummingEventIndex(configuration, 0.375)).toBe(1);
    expect(getActiveStrummingEventIndex(configuration, 0.75)).toBe(2);
    expect(getActiveStrummingEventIndex(configuration, 1.5)).toBe(3);
    expect(getActiveStrummingEventIndex(configuration, 2.25)).toBe(4);
  });
});
