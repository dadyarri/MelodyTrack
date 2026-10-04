import { parseStrummingConfiguration, type StrummingConfiguration } from "@/entities/trainer";

export const trainerConfigurationsStorageKey = "melodytrack.trainers.configurations.v1";
export const strummingTrainerConfigurationId = "strumming";
const legacyStrummingTrainerConfigurationStorageKey = "melodytrack.strummingTrainer.configuration.v1";

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
export function parseStrummingTrainerConfiguration(value: unknown): StrummingConfiguration | null {
  return parseStrummingConfiguration(value);
}
function getBrowserStorage() { try { return window.localStorage; } catch { return null; } }
function readTrainerConfigurations(storage: Storage) {
  try {
    const storedConfigurations = storage.getItem(trainerConfigurationsStorageKey);
    if (storedConfigurations !== null) { const value: unknown = JSON.parse(storedConfigurations); return isRecord(value) ? value : {}; }
    const legacyConfiguration: unknown = JSON.parse(storage.getItem(legacyStrummingTrainerConfigurationStorageKey) ?? "null");
    return legacyConfiguration === null ? {} : { [strummingTrainerConfigurationId]: legacyConfiguration };
  } catch { return {}; }
}
export function readTrainerConfiguration<T>(trainerId: string, parse: (value: unknown) => T | null, storage: Storage | null = getBrowserStorage()) {
  if (!storage) return null;
  return parse(readTrainerConfigurations(storage)[trainerId]);
}
export function writeTrainerConfiguration(trainerId: string, configuration: unknown, storage: Storage | null = getBrowserStorage()) {
  if (!storage) return false;
  try { storage.setItem(trainerConfigurationsStorageKey, JSON.stringify({ ...readTrainerConfigurations(storage), [trainerId]: configuration })); return true; } catch { return false; }
}
