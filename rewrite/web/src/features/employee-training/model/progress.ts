export const trainingVersion = 1;
export const articleId = "staff-access";

export type DemoStep = "signed-out" | "signed-in" | "completed";

export interface TrainingProgress {
  schemaVersion: 1;
  materialVersion: 1;
  staffId: string;
  deferred: boolean;
  articleRead: boolean;
  demoStep: DemoStep;
}

export function createProgress(staffId: string): TrainingProgress {
  return {
    schemaVersion: 1,
    materialVersion: trainingVersion,
    staffId,
    deferred: false,
    articleRead: false,
    demoStep: "signed-out",
  };
}

export function readProgress(value: unknown, staffId: string): TrainingProgress {
  if (value === undefined) return createProgress(staffId);
  if (
    typeof value !== "object" ||
    value === null ||
    !("schemaVersion" in value) ||
    value.schemaVersion !== 1 ||
    !("materialVersion" in value) ||
    value.materialVersion !== trainingVersion ||
    !("staffId" in value) ||
    value.staffId !== staffId ||
    !("deferred" in value) ||
    typeof value.deferred !== "boolean" ||
    !("articleRead" in value) ||
    typeof value.articleRead !== "boolean" ||
    !("demoStep" in value) ||
    !["signed-out", "signed-in", "completed"].includes(String(value.demoStep))
  ) {
    throw new Error("Training progress format is unsupported.");
  }
  return {
    schemaVersion: 1,
    materialVersion: trainingVersion,
    staffId,
    deferred: value.deferred,
    articleRead: value.articleRead,
    demoStep: value.demoStep as DemoStep,
  };
}

export function startDemoSession(current: DemoStep): DemoStep {
  return current === "signed-out" ? "signed-in" : current;
}

export function finishDemoSession(current: DemoStep): DemoStep {
  return current === "signed-in" ? "completed" : current;
}

export function recordDemoStep(progress: TrainingProgress, demoStep: DemoStep): TrainingProgress {
  return progress.demoStep === "completed" ? progress : { ...progress, demoStep };
}
