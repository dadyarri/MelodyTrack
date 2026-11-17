const schemaVersion = 1;
const defaultDatabaseName = "melodytrack-rewrite-offline-v1";
const vaultStoreName = "vaults";
const recordStoreName = "records";
const accessStoreName = "access";
const keyIterations = 600_000;
const keySaltBytes = 16;
const ivBytes = 12;
const dataKeyBytes = 32;
const maximumCiphertextBytes = 5 * 1024 * 1024 + 16;
const cacheRetentionMs = 30 * 24 * 60 * 60 * 1000;
const appliedRetentionMs = 30 * 24 * 60 * 60 * 1000;
const compareAttempts = 5;

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
  readonly secret: string;
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
    super("The offline vault could not be opened with this secret or contains invalid data.");
    this.name = "OfflineVaultCorruptError";
  }
}

interface VaultEnvelope {
  readonly ownerKey: string;
  readonly schemaVersion: number;
  readonly iterations: number;
  readonly salt: Uint8Array;
  readonly iv: Uint8Array;
  readonly ciphertext: Uint8Array;
}

interface RecordEnvelope {
  readonly ownerKey: string;
  readonly id: string;
  readonly schemaVersion: number;
  readonly iv: Uint8Array;
  readonly ciphertext: Uint8Array;
}

interface RecordPayload {
  readonly value: unknown;
  readonly kind: OfflineRecordKind;
  readonly pinned: boolean;
  readonly storedAt: number;
  readonly lastAccessedAt: number;
  readonly sentAt?: number;
}

function getCrypto(): Crypto {
  if (typeof crypto === "undefined" || !crypto.subtle) {
    throw new Error("Secure offline storage is unavailable in this browser.");
  }
  return crypto;
}

function assertStaffId(staffId: string): void {
  if (!staffId.trim()) throw new TypeError("staffId is required.");
}

function assertSecret(secret: string): void {
  if (!secret) throw new TypeError("secret is required.");
}

function assertId(id: string): void {
  if (!id) throw new TypeError("Record id is required.");
}

function accessMarkerState(
  value: unknown,
  ownerKey: string,
): {
  blocked: boolean;
  purgeRequired: boolean;
  malformed: boolean;
} {
  if (value === undefined) return { blocked: false, purgeRequired: false, malformed: false };
  if (!value || typeof value !== "object") {
    return { blocked: true, purgeRequired: false, malformed: true };
  }
  const marker = value as {
    ownerKey?: unknown;
    blocked?: unknown;
    purgeRequired?: unknown;
  };
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

function randomBytes(length: number): Uint8Array {
  return getCrypto().getRandomValues(new Uint8Array(length));
}

function asArrayBuffer(bytes: Uint8Array): ArrayBuffer {
  return Uint8Array.from(bytes).buffer;
}

function isByteArray(
  value: unknown,
  minimumLength: number,
  exactLength?: number,
): value is Uint8Array {
  return (
    value instanceof Uint8Array &&
    value.byteLength >= minimumLength &&
    (exactLength === undefined || value.byteLength === exactLength)
  );
}

async function ownerKeyFor(staffId: string): Promise<string> {
  const digest = await getCrypto().subtle.digest("SHA-256", new TextEncoder().encode(staffId));
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, "0")).join("");
}

function openDatabase(databaseName: string): Promise<IDBDatabase> {
  if (typeof indexedDB === "undefined") {
    return Promise.reject(new Error("IndexedDB is unavailable in this browser."));
  }
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(databaseName, schemaVersion);
    let blocked = false;
    request.onupgradeneeded = () => {
      const database = request.result;
      if (!database.objectStoreNames.contains(vaultStoreName)) {
        database.createObjectStore(vaultStoreName, { keyPath: "ownerKey" });
      }
      if (!database.objectStoreNames.contains(recordStoreName)) {
        const records = database.createObjectStore(recordStoreName, {
          keyPath: ["ownerKey", "id"],
        });
        records.createIndex("byOwner", "ownerKey", { unique: false });
      }
      if (!database.objectStoreNames.contains(accessStoreName)) {
        database.createObjectStore(accessStoreName, { keyPath: "ownerKey" });
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

function keyAad(ownerKey: string): Uint8Array {
  return new TextEncoder().encode(`melodytrack-offline-key:${schemaVersion}:${ownerKey}`);
}

function recordAad(staffId: string, id: string): Uint8Array {
  return new TextEncoder().encode(`melodytrack-offline-record:${schemaVersion}:${staffId}:${id}`);
}

async function deriveWrappingKey(secret: string, salt: Uint8Array): Promise<CryptoKey> {
  const material = await getCrypto().subtle.importKey(
    "raw",
    new TextEncoder().encode(secret),
    "PBKDF2",
    false,
    ["deriveKey"],
  );
  return getCrypto().subtle.deriveKey(
    { name: "PBKDF2", hash: "SHA-256", salt: asArrayBuffer(salt), iterations: keyIterations },
    material,
    { name: "AES-GCM", length: 256 },
    false,
    ["encrypt", "decrypt"],
  );
}

async function createVaultMaterial(
  ownerKey: string,
  secret: string,
): Promise<{ envelope: VaultEnvelope; dataKey: CryptoKey }> {
  const dataKey = randomBytes(dataKeyBytes);
  const salt = randomBytes(keySaltBytes);
  const iv = randomBytes(ivBytes);
  try {
    const wrappingKey = await deriveWrappingKey(secret, salt);
    const ciphertext = new Uint8Array(
      await getCrypto().subtle.encrypt(
        { name: "AES-GCM", iv: asArrayBuffer(iv), additionalData: asArrayBuffer(keyAad(ownerKey)) },
        wrappingKey,
        asArrayBuffer(dataKey),
      ),
    );
    const dataCryptoKey = await getCrypto().subtle.importKey(
      "raw",
      asArrayBuffer(dataKey),
      { name: "AES-GCM" },
      false,
      ["encrypt", "decrypt"],
    );
    return {
      envelope: { ownerKey, schemaVersion, iterations: keyIterations, salt, iv, ciphertext },
      dataKey: dataCryptoKey,
    };
  } finally {
    dataKey.fill(0);
  }
}

function isVaultEnvelope(value: unknown, ownerKey: string): value is VaultEnvelope {
  if (!value || typeof value !== "object") return false;
  const envelope = value as Partial<VaultEnvelope>;
  return (
    envelope.ownerKey === ownerKey &&
    envelope.schemaVersion === schemaVersion &&
    envelope.iterations === keyIterations &&
    isByteArray(envelope.salt, keySaltBytes, keySaltBytes) &&
    isByteArray(envelope.iv, ivBytes, ivBytes) &&
    isByteArray(envelope.ciphertext, dataKeyBytes + 16, dataKeyBytes + 16)
  );
}

function isRecordEnvelope(value: unknown, ownerKey: string, id: string): value is RecordEnvelope {
  if (!value || typeof value !== "object") return false;
  const envelope = value as Partial<RecordEnvelope>;
  return (
    envelope.ownerKey === ownerKey &&
    envelope.id === id &&
    envelope.schemaVersion === schemaVersion &&
    isByteArray(envelope.iv, ivBytes, ivBytes) &&
    isByteArray(envelope.ciphertext, 16) &&
    envelope.ciphertext.byteLength <= maximumCiphertextBytes
  );
}

async function unlockDataKey(secret: string, envelope: VaultEnvelope): Promise<CryptoKey> {
  try {
    const wrappingKey = await deriveWrappingKey(secret, envelope.salt);
    const rawKey = new Uint8Array(
      await getCrypto().subtle.decrypt(
        {
          name: "AES-GCM",
          iv: asArrayBuffer(envelope.iv),
          additionalData: asArrayBuffer(keyAad(envelope.ownerKey)),
        },
        wrappingKey,
        asArrayBuffer(envelope.ciphertext),
      ),
    );
    if (rawKey.byteLength !== dataKeyBytes) {
      rawKey.fill(0);
      throw new OfflineVaultCorruptError();
    }
    try {
      return await getCrypto().subtle.importKey(
        "raw",
        asArrayBuffer(rawKey),
        { name: "AES-GCM" },
        false,
        ["encrypt", "decrypt"],
      );
    } finally {
      rawKey.fill(0);
    }
  } catch {
    throw new OfflineVaultCorruptError();
  }
}

function validPayload(value: unknown): value is RecordPayload {
  if (!value || typeof value !== "object") return false;
  const payload = value as Partial<RecordPayload>;
  return (
    ["cache", "applied", "pending", "conflict"].includes(String(payload.kind)) &&
    typeof payload.pinned === "boolean" &&
    Number.isFinite(payload.storedAt) &&
    Number.isFinite(payload.lastAccessedAt) &&
    (payload.sentAt === undefined || Number.isFinite(payload.sentAt)) &&
    Object.hasOwn(payload, "value")
  );
}

async function encryptRecord(
  ownerKey: string,
  staffId: string,
  id: string,
  dataKey: CryptoKey,
  payload: RecordPayload,
): Promise<RecordEnvelope> {
  const serialized = JSON.stringify(payload);
  if (serialized === undefined)
    throw new TypeError("Offline record value must be JSON serializable.");
  if (!validPayload(JSON.parse(serialized))) {
    throw new TypeError("Offline record does not contain a supported value.");
  }
  const plaintext = new TextEncoder().encode(serialized);
  if (plaintext.byteLength > maximumCiphertextBytes - 16) {
    throw new RangeError("Offline record exceeds the supported size limit.");
  }
  const iv = randomBytes(ivBytes);
  const ciphertext = new Uint8Array(
    await getCrypto().subtle.encrypt(
      {
        name: "AES-GCM",
        iv: asArrayBuffer(iv),
        additionalData: asArrayBuffer(recordAad(staffId, id)),
      },
      dataKey,
      plaintext,
    ),
  );
  return { ownerKey, id, schemaVersion, iv, ciphertext };
}

async function decryptRecord(
  staffId: string,
  id: string,
  dataKey: CryptoKey,
  envelope: unknown,
  ownerKey: string,
): Promise<RecordPayload> {
  if (!isRecordEnvelope(envelope, ownerKey, id)) throw new OfflineVaultCorruptError();
  try {
    const plaintext = await getCrypto().subtle.decrypt(
      {
        name: "AES-GCM",
        iv: asArrayBuffer(envelope.iv),
        additionalData: asArrayBuffer(recordAad(staffId, id)),
      },
      dataKey,
      asArrayBuffer(envelope.ciphertext),
    );
    const payload: unknown = JSON.parse(new TextDecoder().decode(plaintext));
    if (!validPayload(payload)) throw new OfflineVaultCorruptError();
    return payload;
  } catch {
    throw new OfflineVaultCorruptError();
  }
}

function sameEnvelope(left: RecordEnvelope, right: RecordEnvelope): boolean {
  if (
    left.schemaVersion !== right.schemaVersion ||
    left.ownerKey !== right.ownerKey ||
    left.id !== right.id
  )
    return false;
  if (
    left.iv.byteLength !== right.iv.byteLength ||
    left.ciphertext.byteLength !== right.ciphertext.byteLength
  )
    return false;
  return (
    left.iv.every((value, index) => value === right.iv[index]) &&
    left.ciphertext.every((value, index) => value === right.ciphertext[index])
  );
}

function sameVaultEnvelope(left: VaultEnvelope, right: VaultEnvelope): boolean {
  if (
    left.ownerKey !== right.ownerKey ||
    left.schemaVersion !== right.schemaVersion ||
    left.iterations !== right.iterations
  )
    return false;
  if (
    left.salt.byteLength !== right.salt.byteLength ||
    left.iv.byteLength !== right.iv.byteLength ||
    left.ciphertext.byteLength !== right.ciphertext.byteLength
  )
    return false;
  return (
    left.salt.every((value, index) => value === right.salt[index]) &&
    left.iv.every((value, index) => value === right.iv[index]) &&
    left.ciphertext.every((value, index) => value === right.ciphertext[index])
  );
}

async function compareAndSwap(
  databaseName: string,
  ownerKey: string,
  id: string,
  expected: RecordEnvelope,
  expectedVault: VaultEnvelope,
  isActive: () => boolean,
  replacement?: RecordEnvelope,
): Promise<"updated" | "changed" | "blocked"> {
  return withTransaction<"updated" | "changed" | "blocked">(
    databaseName,
    [vaultStoreName, recordStoreName, accessStoreName],
    "readwrite",
    (transaction, complete) => {
      const vaultRequest = transaction.objectStore(vaultStoreName).get(ownerKey);
      vaultRequest.onsuccess = () => {
        if (!isActive()) {
          transaction.abort();
          return;
        }
        const currentVault: unknown = vaultRequest.result;
        if (
          !isVaultEnvelope(currentVault, ownerKey) ||
          !sameVaultEnvelope(currentVault, expectedVault)
        ) {
          complete("changed");
          return;
        }
        const accessRequest = transaction.objectStore(accessStoreName).get(ownerKey);
        accessRequest.onsuccess = () => {
          if (accessMarkerBlocks(accessRequest.result, ownerKey)) {
            complete("blocked");
            return;
          }
          const store = transaction.objectStore(recordStoreName);
          const request = store.get([ownerKey, id]);
          request.onsuccess = () => {
            const current: unknown = request.result;
            if (!isRecordEnvelope(current, ownerKey, id) || !sameEnvelope(current, expected)) {
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

function shouldPrune(payload: RecordPayload, now: number): boolean {
  if (payload.pinned || payload.kind === "pending" || payload.kind === "conflict") return false;
  if (payload.kind === "cache") return now - payload.lastAccessedAt >= cacheRetentionMs;
  return (
    payload.kind === "applied" &&
    payload.sentAt !== undefined &&
    now - payload.sentAt >= appliedRetentionMs
  );
}

function toPublicRecord<T>(id: string, payload: RecordPayload): OfflineRecord<T> {
  return {
    id,
    value: payload.value as T,
    kind: payload.kind,
    pinned: payload.pinned,
    storedAt: payload.storedAt,
    lastAccessedAt: payload.lastAccessedAt,
    ...(payload.sentAt === undefined ? {} : { sentAt: payload.sentAt }),
  };
}

class OfflineVaultImpl implements OfflineVault {
  private dataKey: CryptoKey | undefined;
  private generation = 0;

  constructor(
    private readonly staffId: string,
    private readonly ownerKey: string,
    private readonly databaseName: string,
    private readonly vaultEnvelope: VaultEnvelope,
    dataKey: CryptoKey,
  ) {
    this.dataKey = dataKey;
  }

  lock(): void {
    this.generation += 1;
    this.dataKey = undefined;
  }

  private session(): { key: CryptoKey; generation: number } {
    if (!this.dataKey) throw new OfflineVaultLockedError();
    return { key: this.dataKey, generation: this.generation };
  }

  private assertActive(generation: number): void {
    if (!this.dataKey || generation !== this.generation) throw new OfflineVaultLockedError();
  }

  async get<T = unknown>(id: string): Promise<OfflineRecord<T> | undefined> {
    assertId(id);
    const { key: dataKey, generation } = this.session();
    for (let attempt = 0; attempt < compareAttempts; attempt += 1) {
      const envelope = await readValue<unknown>(this.databaseName, recordStoreName, [
        this.ownerKey,
        id,
      ]);
      this.assertActive(generation);
      if (envelope === undefined) return undefined;
      if (!isRecordEnvelope(envelope, this.ownerKey, id)) throw new OfflineVaultCorruptError();
      const payload = await decryptRecord(this.staffId, id, dataKey, envelope, this.ownerKey);
      this.assertActive(generation);
      if (shouldPrune(payload, Date.now())) {
        const result = await compareAndSwap(
          this.databaseName,
          this.ownerKey,
          id,
          envelope,
          this.vaultEnvelope,
          () => this.dataKey !== undefined && this.generation === generation,
        );
        this.assertActive(generation);
        if (result === "blocked") throw new OfflineVaultAccessBlockedError();
        if (result === "updated") return undefined;
        continue;
      }
      const touched = { ...payload, lastAccessedAt: Date.now() };
      const replacement = await encryptRecord(this.ownerKey, this.staffId, id, dataKey, touched);
      this.assertActive(generation);
      const result = await compareAndSwap(
        this.databaseName,
        this.ownerKey,
        id,
        envelope,
        this.vaultEnvelope,
        () => this.dataKey !== undefined && this.generation === generation,
        replacement,
      );
      if (result === "blocked") throw new OfflineVaultAccessBlockedError();
      if (result === "updated") {
        this.assertActive(generation);
        return toPublicRecord<T>(id, touched);
      }
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
    const { key: dataKey, generation } = this.session();
    const now = Date.now();
    const pinned = record.pinned ?? false;
    if (!isRecordKind(record.kind)) throw new TypeError("Invalid offline record kind.");
    if (record.sentAt !== undefined && !Number.isFinite(record.sentAt))
      throw new TypeError("sentAt must be a finite timestamp.");
    if (record.kind === "applied" && record.sentAt === undefined)
      throw new TypeError("Applied records require sentAt.");
    const payload: RecordPayload = {
      value: record.value,
      kind: record.kind,
      pinned,
      storedAt: now,
      lastAccessedAt: now,
      ...(record.sentAt === undefined ? {} : { sentAt: record.sentAt }),
    };
    const envelope = await encryptRecord(this.ownerKey, this.staffId, record.id, dataKey, payload);
    this.assertActive(generation);
    const outcome = await withTransaction<"written" | "blocked" | "changed">(
      this.databaseName,
      [vaultStoreName, recordStoreName, accessStoreName],
      "readwrite",
      (transaction, complete) => {
        const request = transaction.objectStore(vaultStoreName).get(this.ownerKey);
        request.onsuccess = () => {
          if (!this.dataKey || this.generation !== generation) {
            transaction.abort();
            return;
          }
          const current: unknown = request.result;
          if (
            !isVaultEnvelope(current, this.ownerKey) ||
            !sameVaultEnvelope(current, this.vaultEnvelope)
          ) {
            complete("changed");
            return;
          }
          const accessRequest = transaction.objectStore(accessStoreName).get(this.ownerKey);
          accessRequest.onsuccess = () => {
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
    if (outcome === "blocked") throw new OfflineVaultAccessBlockedError();
    if (outcome === "changed") throw new OfflineVaultLockedError();
  }

  async delete(id: string): Promise<void> {
    assertId(id);
    const { generation } = this.session();
    const outcome = await withTransaction<"deleted" | "blocked" | "changed">(
      this.databaseName,
      [vaultStoreName, recordStoreName, accessStoreName],
      "readwrite",
      (transaction, complete) => {
        const request = transaction.objectStore(vaultStoreName).get(this.ownerKey);
        request.onsuccess = () => {
          if (!this.dataKey || this.generation !== generation) {
            transaction.abort();
            return;
          }
          const current: unknown = request.result;
          if (
            !isVaultEnvelope(current, this.ownerKey) ||
            !sameVaultEnvelope(current, this.vaultEnvelope)
          ) {
            complete("changed");
            return;
          }
          const accessRequest = transaction.objectStore(accessStoreName).get(this.ownerKey);
          accessRequest.onsuccess = () => {
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
    if (outcome === "blocked") throw new OfflineVaultAccessBlockedError();
    if (outcome === "changed") throw new OfflineVaultLockedError();
  }

  async prune(now = Date.now()): Promise<number> {
    const { key: dataKey, generation } = this.session();
    if (!Number.isFinite(now)) throw new TypeError("now must be a finite timestamp.");
    const records = await withTransaction<RecordEnvelope[]>(
      this.databaseName,
      recordStoreName,
      "readonly",
      (transaction, complete) => {
        const request = transaction
          .objectStore(recordStoreName)
          .index("byOwner")
          .getAll(this.ownerKey);
        request.onsuccess = () => complete(request.result);
      },
    );
    let deleted = 0;
    for (const snapshot of records) {
      this.assertActive(generation);
      const id = snapshot.id;
      for (let attempt = 0; attempt < compareAttempts; attempt += 1) {
        const envelope = await readValue<unknown>(this.databaseName, recordStoreName, [
          this.ownerKey,
          id,
        ]);
        this.assertActive(generation);
        if (envelope === undefined) break;
        if (!isRecordEnvelope(envelope, this.ownerKey, id)) throw new OfflineVaultCorruptError();
        const payload = await decryptRecord(this.staffId, id, dataKey, envelope, this.ownerKey);
        this.assertActive(generation);
        if (!shouldPrune(payload, now)) break;
        const result = await compareAndSwap(
          this.databaseName,
          this.ownerKey,
          id,
          envelope,
          this.vaultEnvelope,
          () => this.dataKey !== undefined && this.generation === generation,
        );
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

function isRecordKind(kind: string): kind is OfflineRecordKind {
  return kind === "cache" || kind === "applied" || kind === "pending" || kind === "conflict";
}

export async function createOfflineVault(
  options: CreateOfflineVaultOptions,
): Promise<OfflineVault> {
  const { staffId, secret, databaseName = defaultDatabaseName } = options;
  assertStaffId(staffId);
  assertSecret(secret);
  const ownerKey = await ownerKeyFor(staffId);
  const { envelope, dataKey } = await createVaultMaterial(ownerKey, secret);
  const inserted = await withTransaction<boolean>(
    databaseName,
    vaultStoreName,
    "readwrite",
    (transaction, complete) => {
      const store = transaction.objectStore(vaultStoreName);
      const request = store.get(ownerKey);
      request.onsuccess = () => {
        if (request.result !== undefined) complete(false);
        else {
          store.add(envelope);
          complete(true);
        }
      };
    },
  );
  if (!inserted) throw new OfflineVaultAlreadyExistsError();
  return new OfflineVaultImpl(staffId, ownerKey, databaseName, envelope, dataKey);
}

export async function unlockOfflineVault(options: OfflineVaultOptions): Promise<OfflineVault> {
  const { staffId, secret, databaseName = defaultDatabaseName } = options;
  assertStaffId(staffId);
  assertSecret(secret);
  const ownerKey = await ownerKeyFor(staffId);
  const stored = await readValue<unknown>(databaseName, vaultStoreName, ownerKey);
  if (!isVaultEnvelope(stored, ownerKey)) throw new OfflineVaultCorruptError();
  const dataKey = await unlockDataKey(secret, stored);
  return new OfflineVaultImpl(staffId, ownerKey, databaseName, stored, dataKey);
}

export async function hasOfflineVault(
  staffId: string,
  databaseName = defaultDatabaseName,
): Promise<boolean> {
  assertStaffId(staffId);
  const ownerKey = await ownerKeyFor(staffId);
  return (await readValue<unknown>(databaseName, vaultStoreName, ownerKey)) !== undefined;
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
      transaction.objectStore(vaultStoreName).delete(ownerKey);
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
