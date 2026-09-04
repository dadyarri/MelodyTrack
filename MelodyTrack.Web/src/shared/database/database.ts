import Dexie from "dexie";

export class MelodyTrackDatabase extends Dexie {
  constructor(name = "melodytrack") {
    super(name);
    this.version(1).stores({
      offlineCommands: "[ownerUserId+id], ownerUserId, createdAtUtc, kind",
      offlineIdMappings: "[ownerUserId+temporaryId], ownerUserId, updatedAtUtc",
      drafts: "[ownerUserId+key], ownerUserId, updatedAtUtc, expiresAtUtc",
      referenceData: "[ownerUserId+key], ownerUserId, updatedAtUtc, expiresAtUtc",
      readModels: "[ownerUserId+key], ownerUserId, updatedAtUtc, expiresAtUtc",
      storageMetadata: "key, updatedAtUtc",
    });
    this.version(2).stores({
      offlineCommands: null,
      offlineIdMappings: null,
      drafts: "[ownerUserId+key], ownerUserId, updatedAtUtc, expiresAtUtc",
      referenceData: "[ownerUserId+key], ownerUserId, updatedAtUtc, expiresAtUtc",
      readModels: "[ownerUserId+key], ownerUserId, updatedAtUtc, expiresAtUtc",
      storageMetadata: "key, updatedAtUtc",
    });
  }
}

export const melodyTrackDatabase = new MelodyTrackDatabase();
