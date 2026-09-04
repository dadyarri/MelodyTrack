import * as v from "valibot";

import { melodyTrackDatabase } from "@/shared/database";

import { requireStorageOwnerUserId } from "./owner";

export type FormDraft<TValues> = {
  updatedAtUtc: string;
  values: TValues;
  entityId?: string;
  baselineVersion?: string | null;
};

export type DraftHydrationRef = {
  current: boolean;
};

type DraftRecord = {
  schemaVersion: 2;
  ownerUserId: string;
  key: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  expiresAtUtc: string;
  data: FormDraft<unknown>;
};

export type DraftMetadata = Pick<FormDraft<unknown>, "entityId" | "baselineVersion">;

const schemaVersion = 2;
export const draftRetentionMs = 30 * 24 * 60 * 60 * 1000;
const draftEnvelopeSchema = v.object({
  schemaVersion: v.literal(schemaVersion),
  ownerUserId: v.string(),
  key: v.string(),
  createdAtUtc: v.string(),
  updatedAtUtc: v.string(),
  expiresAtUtc: v.string(),
  data: v.object({
    updatedAtUtc: v.string(),
    values: v.unknown(),
    entityId: v.optional(v.string()),
    baselineVersion: v.optional(v.nullable(v.string())),
  }),
});

export async function loadDraft<TValues>(key: string, schema: v.GenericSchema<unknown, TValues>, owner = requireStorageOwnerUserId()) {
  const ownerUserId = owner;
  const drafts = melodyTrackDatabase.table<DraftRecord, [string, string]>("drafts");
  const record = await drafts.get([ownerUserId, key]);
  if (record) {
    const parsed = v.safeParse(draftEnvelopeSchema, record);
    const values = parsed.success ? v.safeParse(schema, parsed.output.data.values) : null;
    if (
      parsed.success &&
      parsed.output.ownerUserId === ownerUserId &&
      Date.parse(parsed.output.expiresAtUtc) > Date.now() &&
      values?.success
    ) {
      return { ...parsed.output.data, values: values.output } as FormDraft<TValues>;
    }
    const legacyValues = readLegacyIndexedDbValues(record, ownerUserId, key, schema);
    if (legacyValues) {
      return saveDraftValues(key, legacyValues, {}, ownerUserId);
    }
    await drafts.delete([ownerUserId, key]);
  }

  return migrateLegacyDraft(key, schema, ownerUserId);
}

function readLegacyIndexedDbValues<TValues>(value: unknown, ownerUserId: string, key: string, schema: v.GenericSchema<unknown, TValues>) {
  if (!isRecord(value) || value.schemaVersion !== 1 || value.ownerUserId !== ownerUserId || value.key !== key) return null;
  if (typeof value.expiresAtUtc !== "string" || Date.parse(value.expiresAtUtc) <= Date.now() || !isRecord(value.data)) return null;
  const parsed = v.safeParse(schema, value.data.values);
  return parsed.success ? parsed.output : null;
}

export async function saveDraftValues<TValues>(
  key: string,
  values: TValues,
  metadata: DraftMetadata = {},
  owner = requireStorageOwnerUserId(),
) {
  const ownerUserId = owner;
  const drafts = melodyTrackDatabase.table<DraftRecord, [string, string]>("drafts");
  const now = new Date();
  const existing = await drafts.get([ownerUserId, key]);
  const draft: FormDraft<TValues> = {
    updatedAtUtc: now.toISOString(),
    values,
    ...metadata,
  };
  await drafts.put({
    schemaVersion,
    ownerUserId,
    key,
    createdAtUtc: existing?.createdAtUtc ?? now.toISOString(),
    updatedAtUtc: now.toISOString(),
    expiresAtUtc: new Date(now.getTime() + draftRetentionMs).toISOString(),
    data: draft,
  });
  return draft;
}

export async function clearDraft(key: string, owner = requireStorageOwnerUserId()) {
  const ownerUserId = owner;
  await melodyTrackDatabase.table<DraftRecord, [string, string]>("drafts").delete([ownerUserId, key]);
}

export function withDraftHydration(ref: DraftHydrationRef, action: () => void) {
  ref.current = true;
  action();
  queueMicrotask(() => {
    ref.current = false;
  });
}

async function migrateLegacyDraft<TValues>(key: string, schema: v.GenericSchema<unknown, TValues>, ownerUserId: string) {
  const raw = localStorage.getItem(key);
  if (!raw) {
    return null;
  }

  try {
    const parsed = JSON.parse(raw) as unknown;
    const values = isRecord(parsed) ? v.safeParse(schema, parsed.values) : null;
    if (
      !isRecord(parsed) ||
      typeof parsed.updatedAtUtc !== "string" ||
      !values?.success ||
      Date.parse(parsed.updatedAtUtc) + draftRetentionMs <= Date.now()
    ) {
      localStorage.removeItem(key);
      return null;
    }

    const draft = await saveDraftValues(key, values.output, {}, ownerUserId);
    localStorage.removeItem(key);
    return draft;
  } catch {
    localStorage.removeItem(key);
    return null;
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
