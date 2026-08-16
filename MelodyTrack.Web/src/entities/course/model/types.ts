import type { Ulid } from "@/shared/api";

type AppointmentStatus = "planned" | "completed" | "cancelled" | "burned";

export interface CourseSummary {
  id: Ulid;
  name: string;
  description?: string | null;
  blockCount: number;
  themeCount: number;
  updatedAtUtc: string;
}

export interface CourseTheme {
  id: Ulid;
  key: string;
  title: string;
  description?: string | null;
  lessonContent?: string | null;
  homeworkContent?: string | null;
  order: number;
  experiencePointsReward: number;
  dependencyThemeIds: Ulid[];
}

export interface CourseLevel {
  id: Ulid;
  title: string;
  order: number;
  requiredExperiencePoints: number;
}

export interface CourseBranch {
  id: Ulid;
  title: string;
  description?: string | null;
  order: number;
  themes: CourseTheme[];
}

export interface CourseBlock {
  id: Ulid;
  title: string;
  description?: string | null;
  order: number;
  branches: CourseBranch[];
}

export interface Course {
  id: Ulid;
  name: string;
  description?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  levels: CourseLevel[];
  blocks: CourseBlock[];
}

export type CourseThemeProgressState = 0 | 1 | 2 | 3 | 4 | 5;

export interface CourseEnrollmentThemeAppointment {
  id: Ulid;
  startDateUtc: string;
  providerDisplayName?: string | null;
  status: AppointmentStatus;
  lessonNotes?: string | null;
}

export interface CourseEnrollmentTheme {
  id: Ulid;
  courseThemeId: Ulid;
  themeTitle: string;
  themeDescription?: string | null;
  lessonContent?: string | null;
  homeworkContent?: string | null;
  experiencePointsReward: number;
  state: CourseThemeProgressState;
  unlockedAtUtc?: string | null;
  startedAtUtc?: string | null;
  waitingForHomeworkAtUtc?: string | null;
  completedAtUtc?: string | null;
  earnedExperiencePoints: number;
  recentAppointments: CourseEnrollmentThemeAppointment[];
}

export interface CourseEnrollmentLevel {
  id: Ulid;
  title: string;
  order: number;
  requiredExperiencePoints: number;
}

export interface CourseEnrollment {
  id: Ulid;
  clientId: Ulid;
  clientDisplayName: string;
  courseId: Ulid;
  courseName: string;
  createdAtUtc: string;
  course: Course;
  currentLevel?: CourseEnrollmentLevel | null;
  earnedExperiencePoints: number;
  themes: CourseEnrollmentTheme[];
}

export type CourseEnrollmentThemeProgressAction = "unlock" | "start" | "send-to-homework" | "pass-homework" | "return-to-progress";

export interface CourseStructureInput {
  name: string;
  description?: string;
  levels: Array<{
    title: string;
    order: number;
    requiredExperiencePoints: number;
  }>;
  blocks: Array<{
    title: string;
    description?: string;
    order: number;
    branches: Array<{
      title: string;
      description?: string;
      order: number;
      themes: Array<{
        key: string;
        title: string;
        description?: string;
        lessonContent?: string;
        homeworkContent?: string;
        order: number;
        experiencePointsReward: number;
        dependencyKeys: string[];
      }>;
    }>;
  }>;
}
