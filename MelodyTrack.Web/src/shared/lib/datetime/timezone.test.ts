import dayjs from "dayjs";
import { describe, expect, it } from "vitest";

import { teacherTimeToUtc, toTeacherTime } from "./timezone";

describe("teacher schedule timezone", () => {
  it("converts the form's wall time using the teacher's zone", () => {
    expect(teacherTimeToUtc(dayjs("2030-06-03T02:00:00"), "Europe/Moscow")).toBe("2030-06-02T23:00:00.000Z");
  });
  it("displays a utc occurrence on the teacher's local weekday", () => {
    expect(toTeacherTime("2030-06-02T23:00:00Z", "Europe/Moscow").format("YYYY-MM-DD HH:mm")).toBe("2030-06-03 02:00");
  });
  it("chooses the standard offset in an ambiguous local hour", () => {
    expect(teacherTimeToUtc(dayjs("2030-10-27T02:30:00"), "Europe/Berlin")).toBe("2030-10-27T01:30:00.000Z");
  });
  it("rejects a missing local hour at the daylight saving transition", () => {
    expect(() => teacherTimeToUtc(dayjs("2030-03-31T02:30:00"), "Europe/Berlin")).toThrow("Выбранное время отсутствует");
  });
});
