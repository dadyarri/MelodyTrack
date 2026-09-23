import {
  createDefaultStrummingConfiguration,
  handMotions,
  rhythmicValues,
  strumActions,
  type StrummingBar,
  type StrummingConfiguration,
  type StrummingEvent,
  strummingTimeSignatures,
  tempoProgressionUnits,
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

function isStrummingBar(value: unknown): value is StrummingBar {
  return isRecord(value) && Array.isArray(value.events) && value.events.every(isStrummingEvent);
}

function parseMetronome(value: unknown) {
  if (!isRecord(value) || typeof value.enabled !== "boolean" || typeof value.volume !== "number" || value.volume < 0 || value.volume > 1)
    return null;
  return { enabled: value.enabled, volume: value.volume };
}

function parseTempoProgression(value: unknown) {
  if (!isRecord(value) || typeof value.enabled !== "boolean" || typeof value.increment !== "number" || value.increment <= 0) return null;
  if (typeof value.every !== "number" || !Number.isInteger(value.every) || value.every < 1 || typeof value.unit !== "string") return null;
  if (!tempoProgressionUnits.includes(value.unit as (typeof tempoProgressionUnits)[number])) return null;
  return {
    enabled: value.enabled,
    increment: value.increment,
    every: value.every,
    unit: value.unit as (typeof tempoProgressionUnits)[number],
  };
}

export function parseStrummingTrainerConfiguration(value: unknown): StrummingConfiguration | null {
  if (!isRecord(value)) return null;

  const { repetitions } = value;

  if (
    typeof value.bpm !== "number" ||
    !Number.isFinite(value.bpm) ||
    typeof value.timeSignature !== "string" ||
    !strummingTimeSignatures.includes(value.timeSignature as StrummingConfiguration["timeSignature"]) ||
    typeof repetitions !== "number" ||
    !Number.isInteger(repetitions) ||
    repetitions < 1 ||
    typeof value.loop !== "boolean" ||
    ((!Array.isArray(value.bars) || !value.bars.every(isStrummingBar)) &&
      !(
        typeof value.bars === "number" &&
        Number.isInteger(value.bars) &&
        value.bars > 0 &&
        Array.isArray(value.events) &&
        value.events.every(isStrummingEvent)
      ))
  )
    return null;

  const defaults = createDefaultStrummingConfiguration();
  const bars = Array.isArray(value.bars)
    ? value.bars
    : Array.from({ length: value.bars }, () => ({ events: value.events as StrummingEvent[] }));

  return {
    bpm: value.bpm,
    timeSignature: value.timeSignature as StrummingConfiguration["timeSignature"],
    bars,
    repetitions,
    loop: value.loop,
    metronome: parseMetronome(value.metronome) ?? defaults.metronome,
    tempoProgression: parseTempoProgression(value.tempoProgression) ?? defaults.tempoProgression,
  };
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
