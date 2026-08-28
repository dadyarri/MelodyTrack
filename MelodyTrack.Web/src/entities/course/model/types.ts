import type { RequiredApiContract } from "@/shared/api";
import type {
  CourseBlockDto,
  CourseBranchDto,
  CourseDto,
  CourseEnrollmentDto,
  CourseEnrollmentLevelDto,
  CourseEnrollmentThemeAppointmentDto,
  CourseEnrollmentThemeDto,
  CourseLevelDto,
  CourseSummaryDto,
  CourseThemeDto,
  CreateCourseRequest,
} from "@/shared/api/generated/models";

type AppointmentStatus = "planned" | "completed" | "cancelled" | "burned";

export type CourseSummary = RequiredApiContract<CourseSummaryDto, "id" | "name" | "blockCount" | "themeCount" | "updatedAtUtc">;

export type CourseTheme = RequiredApiContract<
  CourseThemeDto,
  "id" | "key" | "title" | "order" | "experiencePointsReward" | "dependencyThemeIds"
>;

export type CourseLevel = RequiredApiContract<CourseLevelDto, "id" | "title" | "order" | "requiredExperiencePoints">;

export type CourseBranch = Omit<RequiredApiContract<CourseBranchDto, "id" | "title" | "order" | "themes">, "themes"> & {
  themes: CourseTheme[];
};

export type CourseBlock = Omit<RequiredApiContract<CourseBlockDto, "id" | "title" | "order" | "branches">, "branches"> & {
  branches: CourseBranch[];
};

export type Course = Omit<
  RequiredApiContract<CourseDto, "id" | "name" | "createdAtUtc" | "updatedAtUtc" | "levels" | "blocks">,
  "levels" | "blocks"
> & {
  levels: CourseLevel[];
  blocks: CourseBlock[];
};

export type CourseThemeProgressState = 0 | 1 | 2 | 3 | 4 | 5;

export type CourseEnrollmentThemeAppointment = Omit<
  RequiredApiContract<CourseEnrollmentThemeAppointmentDto, "id" | "startDateUtc" | "status">,
  "status"
> & {
  status: AppointmentStatus;
};

export type CourseEnrollmentTheme = Omit<
  RequiredApiContract<
    CourseEnrollmentThemeDto,
    "id" | "courseThemeId" | "themeTitle" | "experiencePointsReward" | "state" | "earnedExperiencePoints" | "recentAppointments"
  >,
  "state" | "recentAppointments"
> & {
  state: CourseThemeProgressState;
  recentAppointments: CourseEnrollmentThemeAppointment[];
};

export type CourseEnrollmentLevel = RequiredApiContract<CourseEnrollmentLevelDto, "id" | "title" | "order" | "requiredExperiencePoints">;

export type CourseEnrollment = Omit<
  RequiredApiContract<
    CourseEnrollmentDto,
    "id" | "clientId" | "clientDisplayName" | "courseId" | "courseName" | "createdAtUtc" | "course" | "earnedExperiencePoints" | "themes"
  >,
  "course" | "currentLevel" | "themes"
> & {
  course: Course;
  currentLevel?: CourseEnrollmentLevel | null;
  themes: CourseEnrollmentTheme[];
};

export type CourseEnrollmentThemeProgressAction = "unlock" | "start" | "send-to-homework" | "pass-homework" | "return-to-progress";

export type CourseStructureInput = RequiredApiContract<CreateCourseRequest, "name" | "levels" | "blocks">;
