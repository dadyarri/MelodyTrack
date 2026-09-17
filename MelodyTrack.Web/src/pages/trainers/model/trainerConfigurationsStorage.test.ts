import { describe, expect, it } from "vitest";

import { createDefaultStrummingConfiguration } from "@/entities/trainer";

import {
  parseStrummingTrainerConfiguration,
  readTrainerConfiguration,
  strummingTrainerConfigurationId,
  trainerConfigurationsStorageKey,
  writeTrainerConfiguration,
} from "./trainerConfigurationsStorage";

function createStorage() {
  const values = new Map<string, string>();

  return {
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => values.set(key, value),
  } as unknown as Storage;
}

describe("trainer configurations storage", () => {
  it("restores a validated stored configuration", () => {
    const storage = createStorage();
    const configuration = { ...createDefaultStrummingConfiguration(), bpm: 120 };

    expect(writeTrainerConfiguration(strummingTrainerConfigurationId, configuration, storage)).toBe(true);
    expect(readTrainerConfiguration(strummingTrainerConfigurationId, parseStrummingTrainerConfiguration, storage)).toEqual(configuration);
  });

  it("ignores malformed stored configuration", () => {
    const storage = createStorage();
    storage.setItem(trainerConfigurationsStorageKey, JSON.stringify({ [strummingTrainerConfigurationId]: { bpm: "fast" } }));

    expect(readTrainerConfiguration(strummingTrainerConfigurationId, parseStrummingTrainerConfiguration, storage)).toBeNull();
  });

  it("preserves configurations belonging to other trainers", () => {
    const storage = createStorage();

    writeTrainerConfiguration("chord-changes", { progression: "Am F C G" }, storage);
    writeTrainerConfiguration(strummingTrainerConfigurationId, createDefaultStrummingConfiguration(), storage);

    expect(JSON.parse(storage.getItem(trainerConfigurationsStorageKey) ?? "{}")).toMatchObject({
      "chord-changes": { progression: "Am F C G" },
      [strummingTrainerConfigurationId]: createDefaultStrummingConfiguration(),
    });
  });
});
