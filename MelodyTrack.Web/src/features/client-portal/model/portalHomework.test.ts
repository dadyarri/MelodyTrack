import { describe, expect, it } from "vitest";

import type { CourseEnrollmentTheme, CourseThemeProgressState } from "@/entities/course";

import { getPortalHomework } from "./portalHomework";

function theme(state: CourseThemeProgressState, content: string | null = "Практикуйте аккорды"): CourseEnrollmentTheme {
  return {
    id: `theme-${String(state)}`,
    courseThemeId: "source-theme",
    themeTitle: "Аккорды",
    state,
    homeworkDocument: content,
    practiceExercises: [],
    earnedExperiencePoints: 0,
    experiencePointsReward: 0,
    recentAppointments: [],
  };
}

describe("Portal homework", () => {
  it.each([0, 1, 2, 3, 5] as const)("does not present state %i as assigned homework", (state) => {
    const result = getPortalHomework([{ id: "enrollment", courseName: "Гитара", themes: [theme(state)] }]);

    expect(result).toEqual([]);
  });

  it("preserves the course and assignment identity for teacher-assigned homework", () => {
    const assignment = theme(4);

    const result = getPortalHomework([{ id: "enrollment", courseName: "Гитара", themes: [assignment] }]);

    expect(result).toEqual([{ enrollmentId: "enrollment", courseName: "Гитара", theme: assignment }]);
  });

  it.each([null, "", "  \n "])("omits assignments with empty content %j", (content) => {
    const result = getPortalHomework([{ id: "enrollment", courseName: "Гитара", themes: [theme(4, content)] }]);

    expect(result).toEqual([]);
  });
});
