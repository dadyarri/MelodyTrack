import fsd from "@feature-sliced/steiger-plugin";
import { defineConfig } from "steiger";

const intentionalRouteWidgets = ["course-workspace", "schedule-calendar"];
const intentionalFocusedSlices = [
  "entities/audit-log",
  "entities/release",
  "features/client-portal",
  "features/edit-user",
  "features/enroll-client-course",
  "features/manage-appointment",
  "features/view-release-notes",
  "features/update-course-progress",
];

export default defineConfig([
  ...fsd.configs.recommended,
  {
    files: ["./src/pages/**"],
    rules: {
      "fsd/excessive-slicing": "off",
    },
  },
  ...intentionalFocusedSlices.map((slice) => ({
    files: [`./src/${slice}/**`],
    rules: {
      "fsd/insignificant-slice": "off",
    },
  })),
  ...intentionalRouteWidgets.map((slice) => ({
    files: [`./src/widgets/${slice}/**`],
    rules: {
      "fsd/insignificant-slice": "off",
    },
  })),
]);
