import {
  handMotions,
  rhythmicValues,
  strumActions,
  type StrummingConfiguration,
  type StrummingEvent,
  strummingTimeSignatures,
} from "@/entities/trainer";

export const trainerConfigurationsStorageKey = "melodytrack.trainers.configurations.v1";
export const strummingTrainerConfigurationId = "strumming";
const legacyStrummingTrainerConfigurationStorageKey = "melodytrack.strummingTrainer.configuration.v1";

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isStrummingEvent(value: unknown): value is StrummingEvent {
  return (
    isRecord(value) &&
    typeof value.rhythmicValue === "string" &&
    rhythmicValues.includes(value.rhythmicValue as StrummingEvent["rhythmicValue"]) &&
    typeof value.handMotion === "string" &&
    handMotions.includes(value.handMotion as StrummingEvent["handMotion"]) &&
    typeof value.action === "string" &&
    strumActions.includes(value.action as StrummingEvent["action"]) &&
    typeof value.accent === "boolean"
  );
}

export function parseStrummingTrainerConfiguration(value: unknown): StrummingConfiguration | null {
  if (!isRecord(value)) return null;

  const { bars, repetitions } = value;

  if (
    typeof value.bpm !== "number" ||
    !Number.isFinite(value.bpm) ||
    typeof value.timeSignature !== "string" ||
    !strummingTimeSignatures.includes(value.timeSignature as StrummingConfiguration["timeSignature"]) ||
    typeof bars !== "number" ||
    !Number.isInteger(bars) ||
    bars < 1 ||
    typeof repetitions !== "number" ||
    !Number.isInteger(repetitions) ||
    repetitions < 1 ||
    typeof value.loop !== "boolean" ||
    !Array.isArray(value.events) ||
    !value.events.every(isStrummingEvent)
  )
    return null;

  return value as StrummingConfiguration;
}

function getBrowserStorage() {
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

function readTrainerConfigurations(storage: Storage) {
  try {
    const storedConfigurations = storage.getItem(trainerConfigurationsStorageKey);
    if (storedConfigurations !== null) {
      const value: unknown = JSON.parse(storedConfigurations);
      return isRecord(value) ? value : {};
    }

    const legacyConfiguration: unknown = JSON.parse(storage.getItem(legacyStrummingTrainerConfigurationStorageKey) ?? "null");
    return legacyConfiguration === null ? {} : { [strummingTrainerConfigurationId]: legacyConfiguration };
  } catch {
    return {};
  }
}

export function readTrainerConfiguration<T>(
  trainerId: string,
  parse: (value: unknown) => T | null,
  storage: Storage | null = getBrowserStorage(),
) {
  if (!storage) return null;

  return parse(readTrainerConfigurations(storage)[trainerId]);
}

export function writeTrainerConfiguration(trainerId: string, configuration: unknown, storage: Storage | null = getBrowserStorage()) {
  if (!storage) return false;

  try {
    storage.setItem(trainerConfigurationsStorageKey, JSON.stringify({ ...readTrainerConfigurations(storage), [trainerId]: configuration }));
    return true;
  } catch {
    return false;
  }
}
