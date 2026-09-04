import "fake-indexeddb/auto";

import Dexie from "dexie";
import { afterEach, describe, expect, it } from "vitest";

import { MelodyTrackDatabase } from "./database";

const databaseName = "melodytrack-offline-store-upgrade-test";

afterEach(async () => {
  await Dexie.delete(databaseName);
});

describe("MelodyTrackDatabase upgrades", () => {
  it("drops legacy offline stores without deleting form drafts", async () => {
    const legacyDatabase = new Dexie(databaseName);
    legacyDatabase.version(1).stores({
      offlineCommands: "[ownerUserId+id], ownerUserId, createdAtUtc, kind",
      offlineIdMappings: "[ownerUserId+temporaryId], ownerUserId, updatedAtUtc",
      drafts: "[ownerUserId+key], ownerUserId, updatedAtUtc, expiresAtUtc",
      referenceData: "[ownerUserId+key], ownerUserId, updatedAtUtc, expiresAtUtc",
      readModels: "[ownerUserId+key], ownerUserId, updatedAtUtc, expiresAtUtc",
      storageMetadata: "key, updatedAtUtc",
    });
    await legacyDatabase.table("offlineCommands").add({ ownerUserId: "user-1", id: "command-1", kind: "create-client" });
    await legacyDatabase.table("offlineIdMappings").add({ ownerUserId: "user-1", temporaryId: "temp-1" });
    await legacyDatabase.table("drafts").add({ ownerUserId: "user-1", key: "draft:test", values: { name: "Keep me" } });
    legacyDatabase.close();

    const upgradedDatabase = new MelodyTrackDatabase(databaseName);
    await upgradedDatabase.open();

    expect(upgradedDatabase.tables.map(({ name }) => name)).not.toContain("offlineCommands");
    expect(upgradedDatabase.tables.map(({ name }) => name)).not.toContain("offlineIdMappings");
    expect(await upgradedDatabase.table("drafts").count()).toBe(1);

    upgradedDatabase.close();
  });
});
