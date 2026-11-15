export const trainingVersion = 1;
export const articleId = "staff-access";

export type DemoStep = "signed-out" | "signed-in" | "completed";

export type ClientDirectoryProgress = { version: 1; completed: boolean; deferred: boolean };

export interface TrainingProgress {
  schemaVersion: 1;
  materialVersion: 1;
  staffId: string;
  deferred: boolean;
  articleRead: boolean;
  demoStep: DemoStep;
  clientDirectory: ClientDirectoryProgress;
}

export function createProgress(staffId: string): TrainingProgress {
  return {
    schemaVersion: 1,
    materialVersion: trainingVersion,
    staffId,
    deferred: false,
    articleRead: false,
    demoStep: "signed-out",
    clientDirectory: { version: 1, completed: false, deferred: false },
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
  const directory = "clientDirectory" in value ? value.clientDirectory : undefined;
  if (
    directory !== undefined &&
    (typeof directory !== "object" ||
      directory === null ||
      !("version" in directory) ||
      directory.version !== 1 ||
      !("completed" in directory) ||
      typeof directory.completed !== "boolean" ||
      !("deferred" in directory) ||
      typeof directory.deferred !== "boolean")
  )
    throw new Error("Client directory training format is unsupported.");
  return {
    schemaVersion: 1,
    materialVersion: trainingVersion,
    staffId,
    deferred: value.deferred,
    articleRead: value.articleRead,
    demoStep: value.demoStep as DemoStep,
    clientDirectory: (directory as ClientDirectoryProgress | undefined) ?? {
      version: 1,
      completed: false,
      deferred: false,
    },
  };
}

export function mergeTrainingProgress(
  previous: TrainingProgress,
  next: TrainingProgress,
): TrainingProgress {
  if (previous.staffId !== next.staffId) throw new Error("Training accounts do not match.");
  return {
    ...next,
    demoStep: previous.demoStep === "completed" ? "completed" : next.demoStep,
    articleRead: previous.articleRead || next.articleRead,
    deferred: previous.deferred || next.deferred,
    clientDirectory: {
      version: 1,
      completed: previous.clientDirectory.completed || next.clientDirectory.completed,
      deferred: previous.clientDirectory.deferred || next.clientDirectory.deferred,
    },
  };
}
