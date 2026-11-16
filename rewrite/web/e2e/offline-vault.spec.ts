import { expect, test } from "@playwright/test";

test("encrypts account data, applies retention and rejects stale or tampered storage", async ({
  page,
}) => {
  const databaseName = `offline-vault-${Date.now()}-${Math.random()}`;
  await page.route("**/api/**", (route) => route.fulfill({ status: 404 }));
  await page.goto("/e2e/harness.html");

  const result = await page.evaluate(async (name) => {
    const {
      createOfflineVault,
      deleteOfflineVault,
      hasOfflineVault,
      isOfflineVaultBlocked,
      markOfflineVaultForDeletion,
      needsOfflineVaultDeletion,
      setOfflineVaultBlocked,
      unlockOfflineVault,
      OfflineVaultCorruptError,
      OfflineVaultLockedError,
      OfflineVaultAccessBlockedError,
    } = await import("../src/shared/offline/index");
    const staffId = "019a2c63-8c00-7000-8000-000000000051";
    const otherStaffId = "019a2c63-8c00-7000-8000-000000000052";
    const secret = "synthetic-login-secret";
    const sensitive = "Synthetic Client Note 5f2903b4";
    const now = Date.now();
    const vault = await createOfflineVault({ staffId, secret, databaseName: name });
    await vault.put({ id: "old-cache", value: { note: sensitive }, kind: "cache" });
    await vault.put({ id: "recent-cache", value: { note: sensitive }, kind: "cache" });
    await vault.put({
      id: "pinned-cache",
      value: { note: sensitive },
      kind: "cache",
      pinned: true,
    });
    await vault.put({ id: "pending", value: { note: sensitive }, kind: "pending" });
    await vault.put({ id: "conflict", value: { note: sensitive }, kind: "conflict" });
    await vault.put({
      id: "old-applied",
      value: { note: sensitive },
      kind: "applied",
      sentAt: now,
    });
    await vault.put({
      id: "recent-applied",
      value: { note: sensitive },
      kind: "applied",
      sentAt: now + 10 * 24 * 60 * 60 * 1000,
    });
    const originalNow = Date.now;
    Date.now = () => now - 31 * 24 * 60 * 60 * 1000;
    await vault.put({ id: "expired-on-read-cache", value: { note: sensitive }, kind: "cache" });
    Date.now = originalNow;
    await vault.put({
      id: "expired-on-read-applied",
      value: { note: sensitive },
      kind: "applied",
      sentAt: now - 31 * 24 * 60 * 60 * 1000,
    });

    const storageSnapshot = await new Promise<{ records: unknown[]; vaults: unknown[] }>(
      (resolve, reject) => {
        const request = indexedDB.open(name, 1);
        request.onerror = () => reject(request.error);
        request.onsuccess = () => {
          const database = request.result;
          const transaction = database.transaction(["records", "vaults"], "readonly");
          const records = transaction.objectStore("records").getAll();
          const vaults = transaction.objectStore("vaults").getAll();
          transaction.onerror = () => reject(transaction.error);
          transaction.oncomplete = () => {
            resolve({ records: records.result, vaults: vaults.result });
            database.close();
          };
        };
      },
    );
    const serializedStorage = JSON.stringify(storageSnapshot);
    const recordMetadataOnly = storageSnapshot.records.every(
      (row) =>
        JSON.stringify(Object.keys(row as object).sort()) ===
        JSON.stringify(["ciphertext", "id", "iv", "ownerKey", "schemaVersion"]),
    );
    const wrongSecretRejected = await unlockOfflineVault({
      staffId,
      secret: "wrong-secret",
      databaseName: name,
    }).then(
      () => false,
      (error: unknown) => error instanceof OfflineVaultCorruptError,
    );
    const otherAccount = await createOfflineVault({
      staffId: otherStaffId,
      secret,
      databaseName: name,
    });
    const otherAccountCannotRead = (await otherAccount.get("pending")) === undefined;

    const reopened = await unlockOfflineVault({ staffId, secret, databaseName: name });
    const expiredCacheRemovedOnRead = (await reopened.get("expired-on-read-cache")) === undefined;
    const expiredCommandRemovedOnRead =
      (await reopened.get("expired-on-read-applied")) === undefined;
    await reopened.get("recent-cache");
    const beforeExpiry = await reopened.prune(now + 29 * 24 * 60 * 60 * 1000);
    const afterExpiry = await reopened.prune(now + 31 * 24 * 60 * 60 * 1000);
    const preserved = {
      pinned: (await reopened.get("pinned-cache")) !== undefined,
      pending: (await reopened.get("pending")) !== undefined,
      conflict: (await reopened.get("conflict")) !== undefined,
      recentApplied: (await reopened.get("recent-applied")) !== undefined,
      expiredApplied: (await reopened.get("old-applied")) === undefined,
      accessedCacheExpiredAfterWindow: (await reopened.get("recent-cache")) === undefined,
    };

    const ownerDigest = new Uint8Array(
      await crypto.subtle.digest("SHA-256", new TextEncoder().encode(staffId)),
    );
    const ownerKey = Array.from(ownerDigest, (byte) => byte.toString(16).padStart(2, "0")).join("");
    await new Promise<void>((resolve, reject) => {
      const request = indexedDB.open(name, 1);
      request.onerror = () => reject(request.error);
      request.onsuccess = () => {
        const database = request.result;
        const transaction = database.transaction("records", "readwrite");
        const store = transaction.objectStore("records");
        const read = store.get([ownerKey, "pending"]);
        read.onsuccess = () => {
          const envelope = read.result;
          envelope.ciphertext[0] ^= 1;
          store.put(envelope);
        };
        transaction.oncomplete = () => {
          database.close();
          resolve();
        };
        transaction.onerror = () => reject(transaction.error);
      };
    });
    const tamperRejected = await reopened.get("pending").then(
      () => false,
      (error: unknown) => error instanceof OfflineVaultCorruptError,
    );

    const locked = reopened;
    locked.lock();
    const lockRejected = await locked.get("pending").then(
      () => false,
      (error: unknown) => error instanceof OfflineVaultLockedError,
    );
    const staleWriter = await unlockOfflineVault({ staffId, secret, databaseName: name });
    await setOfflineVaultBlocked(staffId, true, name);
    const blockedReadRejected = await staleWriter.get("pinned-cache").then(
      () => false,
      (error: unknown) => error instanceof OfflineVaultAccessBlockedError,
    );
    const blockedWriteRejected = await staleWriter
      .put({ id: "blocked-write", value: { note: sensitive }, kind: "pending" })
      .then(
        () => false,
        (error: unknown) => error instanceof OfflineVaultAccessBlockedError,
      );
    const blockedDeleteRejected = await staleWriter.delete("pinned-cache").then(
      () => false,
      (error: unknown) => error instanceof OfflineVaultAccessBlockedError,
    );
    await deleteOfflineVault(staffId, name);
    const staleWriteRejected = await staleWriter
      .put({ id: "after-delete", value: { note: sensitive }, kind: "pending" })
      .then(
        () => false,
        () => true,
      );
    const vaultDeleted = !(await hasOfflineVault(staffId, name));
    const markerSurvivedDeletion = await isOfflineVaultBlocked(staffId, name);
    await new Promise<void>((resolve, reject) => {
      const request = indexedDB.open(name, 1);
      request.onerror = () => reject(request.error);
      request.onsuccess = () => {
        const database = request.result;
        const transaction = database.transaction("access", "readwrite");
        transaction.objectStore("access").put({ ownerKey, blocked: "unknown", extra: true });
        transaction.oncomplete = () => {
          database.close();
          resolve();
        };
        transaction.onerror = () => reject(transaction.error);
      };
    });
    const malformedMarkerFailsClosed = await isOfflineVaultBlocked(staffId, name);
    const malformedMarkerNeedsDeletionRejected = await needsOfflineVaultDeletion(
      staffId,
      name,
    ).then(
      () => false,
      (error: unknown) => error instanceof OfflineVaultCorruptError,
    );
    const malformedMarkerPreserved = await new Promise<boolean>((resolve, reject) => {
      const request = indexedDB.open(name, 1);
      request.onerror = () => reject(request.error);
      request.onsuccess = () => {
        const database = request.result;
        const read = database.transaction("access", "readonly").objectStore("access").get(ownerKey);
        read.onerror = () => reject(read.error);
        read.onsuccess = () => {
          const marker = read.result;
          database.close();
          resolve(marker.blocked === "unknown" && marker.extra === true);
        };
      };
    });
    const malformedMarkerResetRejected = await setOfflineVaultBlocked(staffId, false, name).then(
      () => false,
      (error: unknown) => error instanceof OfflineVaultCorruptError,
    );
    await deleteOfflineVault(staffId, name);
    const explicitDeletionClearedPurgeFlag = !(await needsOfflineVaultDeletion(staffId, name));
    const deletionPreservedBlock = await isOfflineVaultBlocked(staffId, name);
    await setOfflineVaultBlocked(staffId, false, name);
    const explicitCredentialResetClearedMarker = !(await isOfflineVaultBlocked(staffId, name));

    await markOfflineVaultForDeletion(staffId, name);
    const purgeFlagBlocksAccess = await isOfflineVaultBlocked(staffId, name);
    await setOfflineVaultBlocked(staffId, false, name);
    const purgeFlagSurvivesUnblock = await needsOfflineVaultDeletion(staffId, name);
    const purgeFlagSurvivesRestartRead = await needsOfflineVaultDeletion(staffId, name);
    await deleteOfflineVault(staffId, name);
    const successfulDeletionClearedPurgeFlag = !(await needsOfflineVaultDeletion(staffId, name));
    const successfulDeletionPreservedBlock = await isOfflineVaultBlocked(staffId, name);
    await setOfflineVaultBlocked(staffId, false, name);
    vault.lock();
    otherAccount.lock();
    staleWriter.lock();
    return {
      plaintextAbsent:
        !serializedStorage.includes(sensitive) &&
        !serializedStorage.includes(secret) &&
        !serializedStorage.includes(staffId),
      recordMetadataOnly,
      wrongSecretRejected,
      otherAccountCannotRead,
      beforeExpiry,
      afterExpiry,
      expiredCacheRemovedOnRead,
      expiredCommandRemovedOnRead,
      preserved,
      tamperRejected,
      lockRejected,
      staleWriteRejected,
      vaultDeleted,
      blockedReadRejected,
      blockedWriteRejected,
      blockedDeleteRejected,
      markerSurvivedDeletion,
      malformedMarkerFailsClosed,
      malformedMarkerNeedsDeletionRejected,
      malformedMarkerPreserved,
      malformedMarkerResetRejected,
      explicitDeletionClearedPurgeFlag,
      deletionPreservedBlock,
      explicitCredentialResetClearedMarker,
      purgeFlagBlocksAccess,
      purgeFlagSurvivesUnblock,
      purgeFlagSurvivesRestartRead,
      successfulDeletionClearedPurgeFlag,
      successfulDeletionPreservedBlock,
    };
  }, databaseName);

  expect(result.plaintextAbsent).toBe(true);
  expect(result.recordMetadataOnly).toBe(true);
  expect(result.wrongSecretRejected).toBe(true);
  expect(result.otherAccountCannotRead).toBe(true);
  expect(result.beforeExpiry).toBe(0);
  expect(result.afterExpiry).toBe(3);
  expect(result.expiredCacheRemovedOnRead).toBe(true);
  expect(result.expiredCommandRemovedOnRead).toBe(true);
  expect(result.preserved).toEqual({
    pinned: true,
    pending: true,
    conflict: true,
    recentApplied: true,
    expiredApplied: true,
    accessedCacheExpiredAfterWindow: true,
  });
  expect(result.tamperRejected).toBe(true);
  expect(result.lockRejected).toBe(true);
  expect(result.staleWriteRejected).toBe(true);
  expect(result.vaultDeleted).toBe(true);
  expect(result.blockedReadRejected).toBe(true);
  expect(result.blockedWriteRejected).toBe(true);
  expect(result.blockedDeleteRejected).toBe(true);
  expect(result.markerSurvivedDeletion).toBe(true);
  expect(result.malformedMarkerFailsClosed).toBe(true);
  expect(result.malformedMarkerNeedsDeletionRejected).toBe(true);
  expect(result.malformedMarkerPreserved).toBe(true);
  expect(result.malformedMarkerResetRejected).toBe(true);
  expect(result.explicitDeletionClearedPurgeFlag).toBe(true);
  expect(result.deletionPreservedBlock).toBe(true);
  expect(result.explicitCredentialResetClearedMarker).toBe(true);
  expect(result.purgeFlagBlocksAccess).toBe(true);
  expect(result.purgeFlagSurvivesUnblock).toBe(true);
  expect(result.purgeFlagSurvivesRestartRead).toBe(true);
  expect(result.successfulDeletionClearedPurgeFlag).toBe(true);
  expect(result.successfulDeletionPreservedBlock).toBe(true);
});
