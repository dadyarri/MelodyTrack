const schemaVersion = 2;
const defaultDatabaseName = "melodytrack-rewrite-offline-v1";
const vaultStoreName = "vaults";
const recordStoreName = "records";
const accessStoreName = "access";
const cacheRetentionMs = 30 * 24 * 60 * 60 * 1000;
const appliedRetentionMs = 30 * 24 * 60 * 60 * 1000;
const compareAttempts = 5;
const maximumRecordBytes = 5 * 1024 * 1024;

export type OfflineRecordKind = "cache" | "applied" | "pending" | "conflict";

export interface OfflineRecord<T = unknown> {
  readonly id: string;
  readonly value: T;
  readonly kind: OfflineRecordKind;
  readonly pinned: boolean;
  readonly storedAt: number;
  readonly lastAccessedAt: number;
  readonly sentAt?: number;
}

export interface OfflineVaultOptions {
  readonly staffId: string;
  readonly databaseName?: string;
}

export type CreateOfflineVaultOptions = OfflineVaultOptions;

export interface OfflineVault {
  get<T = unknown>(id: string): Promise<OfflineRecord<T> | undefined>;
  put<T>(record: {
    readonly id: string;
    readonly value: T;
    readonly kind: OfflineRecordKind;
    readonly pinned?: boolean;
    readonly sentAt?: number;
  }): Promise<void>;
  delete(id: string): Promise<void>;
  prune(now?: number): Promise<number>;
  lock(): void;
}

interface VaultMarker {
  readonly ownerKey: string;
  readonly schemaVersion: number;
  readonly generation: number;
  readonly active: boolean;
}

interface RecordEnvelope {
  readonly ownerKey: string;
  readonly id: string;
  readonly schemaVersion: number;
  readonly generation: number;
  readonly value: unknown;
  readonly kind: OfflineRecordKind;
  readonly pinned: boolean;
  readonly storedAt: number;
  readonly lastAccessedAt: number;
  readonly sentAt?: number;
}

interface AccessMarker {
  readonly ownerKey: string;
  readonly blocked: boolean;
  readonly purgeRequired?: boolean;
}

export class OfflineVaultAlreadyExistsError extends Error {
  constructor() {
    super("An offline vault already exists for this account.");
    this.name = "OfflineVaultAlreadyExistsError";
  }
}

export class OfflineVaultLockedError extends Error {
  constructor() {
    super("The offline vault is locked.");
    this.name = "OfflineVaultLockedError";
  }
}

export class OfflineVaultAccessBlockedError extends Error {
  constructor() {
    super("Local access to this account's offline data is blocked.");
    this.name = "OfflineVaultAccessBlockedError";
  }
}

export class OfflineVaultCorruptError extends Error {
  constructor() {
    super("The offline data has an unsupported or invalid format.");
    this.name = "OfflineVaultCorruptError";
  }
}

function assertStaffId(staffId: string): void {
  if (!staffId.trim()) throw new TypeError("staffId is required.");
}

function assertId(id: string): void {
  if (!id) throw new TypeError("Record id is required.");
}

async function ownerKeyFor(staffId: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(staffId));
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, "0")).join("");
}

function accessMarkerState(
  value: unknown,
  ownerKey: string,
): { blocked: boolean; purgeRequired: boolean; malformed: boolean } {
  if (value === undefined) return { blocked: false, purgeRequired: false, malformed: false };
  if (!value || typeof value !== "object")
    return { blocked: true, purgeRequired: false, malformed: true };
  const marker = value as Partial<AccessMarker>;
  const malformed =
    marker.ownerKey !== ownerKey ||
    typeof marker.blocked !== "boolean" ||
    (marker.purgeRequired !== undefined && typeof marker.purgeRequired !== "boolean") ||
    Object.keys(value).some(
      (key) => key !== "ownerKey" && key !== "blocked" && key !== "purgeRequired",
    );
  if (malformed) return { blocked: true, purgeRequired: false, malformed: true };
  const purgeRequired = marker.purgeRequired === true;
  return { blocked: marker.blocked === true || purgeRequired, purgeRequired, malformed: false };
}

function accessMarkerBlocks(value: unknown, ownerKey: string): boolean {
  return accessMarkerState(value, ownerKey).blocked;
}

function isVaultMarker(value: unknown, ownerKey: string): value is VaultMarker {
  if (!value || typeof value !== "object") return false;
  const marker = value as Partial<VaultMarker>;
  return (
    marker.ownerKey === ownerKey &&
    marker.schemaVersion === schemaVersion &&
    Number.isSafeInteger(marker.generation) &&
    (marker.generation ?? 0) >= 0 &&
    typeof marker.active === "boolean"
  );
}

function isRecordKind(kind: unknown): kind is OfflineRecordKind {
  return kind === "cache" || kind === "applied" || kind === "pending" || kind === "conflict";
}

function isRecordEnvelope(value: unknown, ownerKey: string, id: string): value is RecordEnvelope {
  if (!value || typeof value !== "object") return false;
  const record = value as Partial<RecordEnvelope>;
  return (
    record.ownerKey === ownerKey &&
    record.id === id &&
    record.schemaVersion === schemaVersion &&
    Number.isSafeInteger(record.generation) &&
    (record.generation ?? -1) >= 0 &&
    isRecordKind(record.kind) &&
    typeof record.pinned === "boolean" &&
    Number.isFinite(record.storedAt) &&
    Number.isFinite(record.lastAccessedAt) &&
    (record.sentAt === undefined || Number.isFinite(record.sentAt)) &&
    Object.hasOwn(value, "value")
  );
}

function openDatabase(databaseName: string): Promise<IDBDatabase> {
  if (typeof indexedDB === "undefined") {
    return Promise.reject(new Error("IndexedDB is unavailable in this browser."));
  }
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(databaseName, schemaVersion);
    let blocked = false;
    request.onupgradeneeded = (event) => {
      const database = request.result;
      const transaction = request.transaction;
      if (!database.objectStoreNames.contains(vaultStoreName)) {
        database.createObjectStore(vaultStoreName, { keyPath: "ownerKey" });
      }
      if (!database.objectStoreNames.contains(recordStoreName)) {
        const records = database.createObjectStore(recordStoreName, {
          keyPath: ["ownerKey", "id"],
        });
        records.createIndex("byOwner", "ownerKey", { unique: false });
      } else if (event.oldVersion < schemaVersion) {
        transaction?.objectStore(recordStoreName).clear();
      }
      if (!database.objectStoreNames.contains(accessStoreName)) {
        database.createObjectStore(accessStoreName, { keyPath: "ownerKey" });
      }
      if (event.oldVersion < schemaVersion) {
        // Legacy records are encrypted and intentionally discarded during the format change.
        transaction?.objectStore(vaultStoreName).clear();
      }
    };
    request.onerror = () => reject(new Error("Offline storage could not be opened."));
    request.onblocked = () => {
      blocked = true;
      reject(new Error("Offline storage is blocked by another tab."));
    };
    request.onsuccess = () => {
      const database = request.result;
      if (blocked) {
        database.close();
        return;
      }
      database.onversionchange = () => database.close();
      resolve(database);
    };
  });
}

async function withTransaction<T>(
  databaseName: string,
  stores: string | string[],
  mode: IDBTransactionMode,
  enqueue: (transaction: IDBTransaction, complete: (value: T) => void) => void,
): Promise<T> {
  const database = await openDatabase(databaseName);
  return new Promise<T>((resolve, reject) => {
    let value!: T;
    let transaction: IDBTransaction;
    try {
      transaction = database.transaction(stores, mode);
      enqueue(transaction, (result) => {
        value = result;
      });
    } catch {
      database.close();
      reject(new Error("Offline storage transaction could not be started."));
      return;
    }
    transaction.oncomplete = () => {
      database.close();
      resolve(value);
    };
    transaction.onabort = () => {
      database.close();
      reject(new Error("Offline storage transaction was aborted."));
    };
  });
}

async function readValue<T>(
  databaseName: string,
  storeName: string,
  key: IDBValidKey,
): Promise<T | undefined> {
  return withTransaction<T | undefined>(
    databaseName,
    storeName,
    "readonly",
    (transaction, complete) => {
      const request = transaction.objectStore(storeName).get(key);
      request.onsuccess = () => complete(request.result as T | undefined);
    },
  );
}

function toEnvelope(
  ownerKey: string,
  generation: number,
  record: {
    readonly id: string;
    readonly value: unknown;
    readonly kind: OfflineRecordKind;
    readonly pinned?: boolean;
    readonly storedAt: number;
    readonly lastAccessedAt: number;
    readonly sentAt?: number;
  },
): RecordEnvelope {
  const serialized = JSON.stringify(record.value);
  if (serialized === undefined)
    throw new TypeError("Offline record value must be JSON serializable.");
  if (new TextEncoder().encode(serialized).byteLength > maximumRecordBytes)
    throw new RangeError("Offline record exceeds the supported size limit.");
  return {
    ownerKey,
    id: record.id,
    schemaVersion,
    generation,
    value: JSON.parse(serialized) as unknown,
    kind: record.kind,
    pinned: record.pinned ?? false,
    storedAt: record.storedAt,
    lastAccessedAt: record.lastAccessedAt,
    ...(record.sentAt === undefined ? {} : { sentAt: record.sentAt }),
  };
}

function sameRecord(left: RecordEnvelope, right: RecordEnvelope): boolean {
  return JSON.stringify(left) === JSON.stringify(right);
}

function shouldPrune(record: RecordEnvelope, now: number): boolean {
  if (record.pinned || record.kind === "pending" || record.kind === "conflict") return false;
  if (record.kind === "cache") return now - record.lastAccessedAt >= cacheRetentionMs;
  return (
    record.kind === "applied" &&
    record.sentAt !== undefined &&
    now - record.sentAt >= appliedRetentionMs
  );
}

function toPublicRecord<T>(record: RecordEnvelope): OfflineRecord<T> {
  return {
    id: record.id,
    value: record.value as T,
    kind: record.kind,
    pinned: record.pinned,
    storedAt: record.storedAt,
    lastAccessedAt: record.lastAccessedAt,
    ...(record.sentAt === undefined ? {} : { sentAt: record.sentAt }),
  };
}

async function compareAndSwap(
  databaseName: string,
  ownerKey: string,
  id: string,
  generation: number,
  expected: RecordEnvelope,
  isActive: () => boolean,
  replacement?: RecordEnvelope,
): Promise<"updated" | "changed" | "blocked" | "inactive"> {
  return withTransaction(
    databaseName,
    [vaultStoreName, recordStoreName, accessStoreName],
    "readwrite",
    (transaction, complete) => {
      const markerRequest = transaction.objectStore(vaultStoreName).get(ownerKey);
      markerRequest.onsuccess = () => {
        if (!isActive()) {
          complete("inactive");
          return;
        }
        if (
          !isVaultMarker(markerRequest.result, ownerKey) ||
          markerRequest.result.generation !== generation
        ) {
          complete("changed");
          return;
        }
        const accessRequest = transaction.objectStore(accessStoreName).get(ownerKey);
        accessRequest.onsuccess = () => {
          if (!isActive()) {
            complete("inactive");
            return;
          }
          if (accessMarkerBlocks(accessRequest.result, ownerKey)) {
            complete("blocked");
            return;
          }
          const store = transaction.objectStore(recordStoreName);
          const request = store.get([ownerKey, id]);
          request.onsuccess = () => {
            if (!isActive()) {
              complete("inactive");
              return;
            }
            const current: unknown = request.result;
            if (!isRecordEnvelope(current, ownerKey, id) || !sameRecord(current, expected)) {
              complete("changed");
            } else if (replacement) {
              store.put(replacement);
              complete("updated");
            } else {
              store.delete([ownerKey, id]);
              complete("updated");
            }
          };
        };
      };
    },
  );
}

class OfflineVaultImpl implements OfflineVault {
  private generation = 0;
  private closed = false;

  constructor(
    private readonly ownerKey: string,
    private readonly databaseName: string,
    private readonly vaultGeneration: number,
  ) {}

  lock(): void {
    this.closed = true;
    this.generation += 1;
  }

  private session(): number {
    if (this.closed) throw new OfflineVaultLockedError();
    return this.generation;
  }

  private assertActive(generation: number): void {
    if (generation !== this.generation) throw new OfflineVaultLockedError();
  }

  private async assertPersistent(generation: number): Promise<void> {
    this.assertActive(generation);
    const blockedMarker = await readValue<unknown>(
      this.databaseName,
      accessStoreName,
      this.ownerKey,
    );
    this.assertActive(generation);
    if (accessMarkerBlocks(blockedMarker, this.ownerKey))
      throw new OfflineVaultAccessBlockedError();
    const marker = await readValue<unknown>(this.databaseName, vaultStoreName, this.ownerKey);
    this.assertActive(generation);
    if (!isVaultMarker(marker, this.ownerKey) || marker.generation !== this.vaultGeneration)
      throw new OfflineVaultLockedError();
  }

  async get<T = unknown>(id: string): Promise<OfflineRecord<T> | undefined> {
    assertId(id);
    const generation = this.session();
    for (let attempt = 0; attempt < compareAttempts; attempt += 1) {
      await this.assertPersistent(generation);
      const record = await readValue<unknown>(this.databaseName, recordStoreName, [
        this.ownerKey,
        id,
      ]);
      this.assertActive(generation);
      if (record === undefined) return undefined;
      if (!isRecordEnvelope(record, this.ownerKey, id)) throw new OfflineVaultCorruptError();
      if (record.generation !== this.vaultGeneration) return undefined;
      if (shouldPrune(record, Date.now())) {
        const result = await compareAndSwap(
          this.databaseName,
          this.ownerKey,
          id,
          this.vaultGeneration,
          record,
          () => this.generation === generation,
        );
        this.assertActive(generation);
        if (result === "inactive") throw new OfflineVaultLockedError();
        if (result === "blocked") throw new OfflineVaultAccessBlockedError();
        if (result === "changed") continue;
        return undefined;
      }
      const touched = toEnvelope(this.ownerKey, this.vaultGeneration, {
        ...record,
        lastAccessedAt: Date.now(),
      });
      const result = await compareAndSwap(
        this.databaseName,
        this.ownerKey,
        id,
        this.vaultGeneration,
        record,
        () => this.generation === generation,
        touched,
      );
      this.assertActive(generation);
      if (result === "inactive") throw new OfflineVaultLockedError();
      if (result === "blocked") throw new OfflineVaultAccessBlockedError();
      if (result === "updated") return toPublicRecord<T>(touched);
    }
    throw new Error("Offline record changed repeatedly while being read.");
  }

  async put<T>(record: {
    readonly id: string;
    readonly value: T;
    readonly kind: OfflineRecordKind;
    readonly pinned?: boolean;
    readonly sentAt?: number;
  }): Promise<void> {
    assertId(record.id);
    const generation = this.session();
    if (!isRecordKind(record.kind)) throw new TypeError("Invalid offline record kind.");
    if (record.sentAt !== undefined && !Number.isFinite(record.sentAt))
      throw new TypeError("sentAt must be a finite timestamp.");
    if (record.kind === "applied" && record.sentAt === undefined)
      throw new TypeError("Applied records require sentAt.");
    const envelope = toEnvelope(this.ownerKey, this.vaultGeneration, {
      ...record,
      storedAt: Date.now(),
      lastAccessedAt: Date.now(),
    });
    await this.assertPersistent(generation);
    const outcome = await withTransaction<"written" | "blocked" | "changed" | "inactive">(
      this.databaseName,
      [vaultStoreName, recordStoreName, accessStoreName],
      "readwrite",
      (transaction, complete) => {
        const markerRequest = transaction.objectStore(vaultStoreName).get(this.ownerKey);
        markerRequest.onsuccess = () => {
          if (this.generation !== generation) {
            complete("inactive");
            return;
          }
          if (
            !isVaultMarker(markerRequest.result, this.ownerKey) ||
            markerRequest.result.generation !== this.vaultGeneration
          ) {
            complete("changed");
            return;
          }
          const accessRequest = transaction.objectStore(accessStoreName).get(this.ownerKey);
          accessRequest.onsuccess = () => {
            if (this.generation !== generation) {
              complete("inactive");
              return;
            }
            if (accessMarkerBlocks(accessRequest.result, this.ownerKey)) {
              complete("blocked");
              return;
            }
            transaction.objectStore(recordStoreName).put(envelope);
            complete("written");
          };
        };
      },
    );
    this.assertActive(generation);
    if (outcome === "inactive") throw new OfflineVaultLockedError();
    if (outcome === "blocked") throw new OfflineVaultAccessBlockedError();
    if (outcome === "changed") throw new OfflineVaultLockedError();
  }

  async delete(id: string): Promise<void> {
    assertId(id);
    const generation = this.session();
    await this.assertPersistent(generation);
    const outcome = await withTransaction<"deleted" | "blocked" | "changed" | "inactive">(
      this.databaseName,
      [vaultStoreName, recordStoreName, accessStoreName],
      "readwrite",
      (transaction, complete) => {
        const markerRequest = transaction.objectStore(vaultStoreName).get(this.ownerKey);
        markerRequest.onsuccess = () => {
          if (this.generation !== generation) {
            complete("inactive");
            return;
          }
          if (
            !isVaultMarker(markerRequest.result, this.ownerKey) ||
            markerRequest.result.generation !== this.vaultGeneration
          ) {
            complete("changed");
            return;
          }
          const accessRequest = transaction.objectStore(accessStoreName).get(this.ownerKey);
          accessRequest.onsuccess = () => {
            if (this.generation !== generation) {
              complete("inactive");
              return;
            }
            if (accessMarkerBlocks(accessRequest.result, this.ownerKey)) {
              complete("blocked");
              return;
            }
            transaction.objectStore(recordStoreName).delete([this.ownerKey, id]);
            complete("deleted");
          };
        };
      },
    );
    this.assertActive(generation);
    if (outcome === "inactive") throw new OfflineVaultLockedError();
    if (outcome === "blocked") throw new OfflineVaultAccessBlockedError();
    if (outcome === "changed") throw new OfflineVaultLockedError();
  }

  async prune(now = Date.now()): Promise<number> {
    const generation = this.session();
    if (!Number.isFinite(now)) throw new TypeError("now must be a finite timestamp.");
    await this.assertPersistent(generation);
    const records = await withTransaction<RecordEnvelope[]>(
      this.databaseName,
      recordStoreName,
      "readonly",
      (transaction, complete) => {
        const request = transaction
          .objectStore(recordStoreName)
          .index("byOwner")
          .getAll(this.ownerKey);
        request.onsuccess = () => complete(request.result as RecordEnvelope[]);
      },
    );
    let deleted = 0;
    for (const snapshot of records) {
      this.assertActive(generation);
      if (!isRecordEnvelope(snapshot, this.ownerKey, snapshot.id))
        throw new OfflineVaultCorruptError();
      if (snapshot.generation !== this.vaultGeneration || !shouldPrune(snapshot, now)) continue;
      for (let attempt = 0; attempt < compareAttempts; attempt += 1) {
        const current = await readValue<unknown>(this.databaseName, recordStoreName, [
          this.ownerKey,
          snapshot.id,
        ]);
        this.assertActive(generation);
        if (current === undefined) break;
        if (!isRecordEnvelope(current, this.ownerKey, snapshot.id))
          throw new OfflineVaultCorruptError();
        if (!shouldPrune(current, now)) break;
        const result = await compareAndSwap(
          this.databaseName,
          this.ownerKey,
          current.id,
          this.vaultGeneration,
          current,
          () => this.generation === generation,
        );
        if (result === "inactive") throw new OfflineVaultLockedError();
        if (result === "blocked") throw new OfflineVaultAccessBlockedError();
        if (result === "updated") {
          deleted += 1;
          break;
        }
      }
    }
    return deleted;
  }
}

export async function createOfflineVault(
  options: CreateOfflineVaultOptions,
): Promise<OfflineVault> {
  const { staffId, databaseName = defaultDatabaseName } = options;
  assertStaffId(staffId);
  const ownerKey = await ownerKeyFor(staffId);
  const marker = await withTransaction<VaultMarker | undefined>(
    databaseName,
    vaultStoreName,
    "readwrite",
    (transaction, complete) => {
      const store = transaction.objectStore(vaultStoreName);
      const request = store.get(ownerKey);
      request.onsuccess = () => {
        const current: unknown = request.result;
        if (isVaultMarker(current, ownerKey) && current.active) {
          complete(undefined);
          return;
        }
        const previousGeneration = isVaultMarker(current, ownerKey) ? current.generation : 0;
        const created: VaultMarker = {
          ownerKey,
          schemaVersion,
          generation: previousGeneration + 1,
          active: true,
        };
        store.put(created);
        complete(created);
      };
    },
  );
  if (!marker) throw new OfflineVaultAlreadyExistsError();
  return new OfflineVaultImpl(ownerKey, databaseName, marker.generation);
}

export async function unlockOfflineVault(options: OfflineVaultOptions): Promise<OfflineVault> {
  const { staffId, databaseName = defaultDatabaseName } = options;
  assertStaffId(staffId);
  const ownerKey = await ownerKeyFor(staffId);
  const marker = await readValue<unknown>(databaseName, vaultStoreName, ownerKey);
  if (!isVaultMarker(marker, ownerKey) || !marker.active) throw new OfflineVaultCorruptError();
  return new OfflineVaultImpl(ownerKey, databaseName, marker.generation);
}

export async function hasOfflineVault(
  staffId: string,
  databaseName = defaultDatabaseName,
): Promise<boolean> {
  assertStaffId(staffId);
  const ownerKey = await ownerKeyFor(staffId);
  const stored = await readValue<unknown>(databaseName, vaultStoreName, ownerKey);
  return isVaultMarker(stored, ownerKey) && stored.active;
}

export async function isOfflineVaultBlocked(
  staffId: string,
  databaseName = defaultDatabaseName,
): Promise<boolean> {
  assertStaffId(staffId);
  const ownerKey = await ownerKeyFor(staffId);
  const stored = await readValue<unknown>(databaseName, accessStoreName, ownerKey);
  return accessMarkerBlocks(stored, ownerKey);
}

export async function setOfflineVaultBlocked(
  staffId: string,
  blocked: boolean,
  databaseName = defaultDatabaseName,
): Promise<void> {
  assertStaffId(staffId);
  if (typeof blocked !== "boolean") throw new TypeError("blocked must be a boolean.");
  const ownerKey = await ownerKeyFor(staffId);
  const updated = await withTransaction<boolean>(
    databaseName,
    accessStoreName,
    "readwrite",
    (transaction, complete) => {
      const store = transaction.objectStore(accessStoreName);
      const request = store.get(ownerKey);
      request.onsuccess = () => {
        const current = accessMarkerState(request.result, ownerKey);
        if (current.malformed) {
          complete(false);
          return;
        }
        store.put({ ownerKey, blocked, ...(current.purgeRequired ? { purgeRequired: true } : {}) });
        complete(true);
      };
    },
  );
  if (!updated) throw new OfflineVaultCorruptError();
}

export async function markOfflineVaultForDeletion(
  staffId: string,
  databaseName = defaultDatabaseName,
): Promise<void> {
  assertStaffId(staffId);
  const ownerKey = await ownerKeyFor(staffId);
  await withTransaction<void>(
    databaseName,
    accessStoreName,
    "readwrite",
    (transaction, complete) => {
      transaction
        .objectStore(accessStoreName)
        .put({ ownerKey, blocked: true, purgeRequired: true });
      complete(undefined);
    },
  );
}

export async function needsOfflineVaultDeletion(
  staffId: string,
  databaseName = defaultDatabaseName,
): Promise<boolean> {
  assertStaffId(staffId);
  const ownerKey = await ownerKeyFor(staffId);
  const stored = await readValue<unknown>(databaseName, accessStoreName, ownerKey);
  const marker = accessMarkerState(stored, ownerKey);
  if (marker.malformed) throw new OfflineVaultCorruptError();
  return marker.purgeRequired;
}

export async function deleteOfflineVault(
  staffId: string,
  databaseName = defaultDatabaseName,
): Promise<void> {
  assertStaffId(staffId);
  const ownerKey = await ownerKeyFor(staffId);
  await withTransaction<void>(
    databaseName,
    [vaultStoreName, recordStoreName, accessStoreName],
    "readwrite",
    (transaction, complete) => {
      const vaults = transaction.objectStore(vaultStoreName);
      const vaultRequest = vaults.get(ownerKey);
      vaultRequest.onsuccess = () => {
        const current: unknown = vaultRequest.result;
        const generation = isVaultMarker(current, ownerKey) ? current.generation : 0;
        vaults.put({
          ownerKey,
          schemaVersion,
          generation: generation + 1,
          active: false,
        } satisfies VaultMarker);
      };
      const accessStore = transaction.objectStore(accessStoreName);
      const accessRequest = accessStore.get(ownerKey);
      accessRequest.onsuccess = () => {
        const marker = accessMarkerState(accessRequest.result, ownerKey);
        accessStore.put({ ownerKey, blocked: marker.blocked, purgeRequired: false });
      };
      const records = transaction.objectStore(recordStoreName);
      const request = records.index("byOwner").openKeyCursor(IDBKeyRange.only(ownerKey));
      request.onsuccess = () => {
        const cursor = request.result;
        if (!cursor) return;
        records.delete(cursor.primaryKey);
        cursor.continue();
      };
      complete(undefined);
    },
  );
}
