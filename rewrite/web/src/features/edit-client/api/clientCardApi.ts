import { DateOnly } from "@microsoft/kiota-abstractions";
import type { DirectoryClient } from "@/entities/client-directory";
import type { applicationApi } from "@/shared/api";
import { apiOptions, invalidResponse, isObject } from "@/shared/api";
import type { ClientCardDraft } from "../ui/ClientEditorDrawer";
import type { ClientNoteHistoryEntry } from "../ui/ClientNotesSection";

type Api = ReturnType<typeof applicationApi>;

function text(value: unknown): string {
  if (typeof value !== "string" || !value.length) throw invalidResponse();
  return value;
}

function nullableText(value: unknown): string | null {
  if (value === null || value === undefined) return null;
  if (typeof value !== "string") throw invalidResponse();
  return value;
}

function readCard(value: unknown): DirectoryClient {
  if (!isObject(value) || !Array.isArray(value.contacts) || typeof value.isArchived !== "boolean")
    throw invalidResponse();
  const createdAt =
    value.createdAt instanceof Date ? value.createdAt.toISOString() : text(value.createdAt);
  if (!Number.isFinite(Date.parse(createdAt))) throw invalidResponse();
  const birthdate =
    value.birthdate instanceof DateOnly ? value.birthdate.toString() : value.birthdate;
  if (
    birthdate !== null &&
    birthdate !== undefined &&
    (typeof birthdate !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(birthdate))
  )
    throw invalidResponse();
  return {
    id: text(value.id),
    firstName: text(value.firstName),
    lastName: text(value.lastName),
    middleName: nullableText(value.middleName),
    birthdate: birthdate ?? null,
    sourceId: nullableText(value.sourceId),
    sourceName: nullableText(value.sourceName),
    isArchived: value.isArchived,
    createdAt,
    contacts: value.contacts.map((contact: unknown) => {
      if (!isObject(contact)) throw invalidResponse();
      const format = contact.format;
      if (format !== "phone" && format !== "email" && format !== "link") throw invalidResponse();
      if (typeof contact.isPrimary !== "boolean") throw invalidResponse();
      return {
        id: text(contact.id),
        kindId: text(contact.kindId),
        kindName: text(contact.kindName),
        format,
        value: text(contact.value),
        comment: nullableText(contact.comment),
        isPrimary: contact.isPrimary,
      };
    }),
  };
}

function values(draft: ClientCardDraft) {
  return {
    firstName: draft.firstName,
    lastName: draft.lastName,
    middleName: draft.middleName,
    birthdate: draft.birthdate ? DateOnly.parse(draft.birthdate) : null,
    sourceId: draft.sourceId,
    contacts: draft.contacts.map((contact) => ({ ...contact })),
  };
}

function expectedValues(client: DirectoryClient) {
  return {
    firstName: client.firstName,
    lastName: client.lastName,
    middleName: client.middleName,
    birthdate: client.birthdate ? DateOnly.parse(client.birthdate) : null,
    sourceId: client.sourceId,
    contacts: client.contacts.map(({ id, kindId, value, comment, isPrimary }) => ({
      id,
      kindId,
      value,
      comment,
      isPrimary,
    })),
  };
}

export async function getClientCard(api: Api, signal: AbortSignal, clientId: string) {
  const response = await api.clients.byClientId(clientId).get(apiOptions(signal));
  if (!response) throw invalidResponse();
  return readCard(response);
}

export async function createClientCard(
  api: Api,
  signal: AbortSignal,
  draft: ClientCardDraft,
  command: { commandId: string; clientId: string },
) {
  const response = await api.clients.post(
    {
      commandId: command.commandId,
      clientId: command.clientId,
      ...values(draft),
    },
    apiOptions(signal),
  );
  if (!response?.client) throw invalidResponse();
  return {
    client: readCard(response.client),
    hasDuplicateContacts: response.hasDuplicateContacts === true,
  };
}

export async function updateClientCard(
  api: Api,
  signal: AbortSignal,
  client: DirectoryClient,
  draft: ClientCardDraft,
  commandId: string,
) {
  const response = await api.clients.byClientId(client.id).put(
    {
      commandId,
      expectedState: expectedValues(client),
      ...values(draft),
    },
    apiOptions(signal),
  );
  if (!response) throw invalidResponse();
  return response.hasDuplicateContacts === true;
}

export async function restoreClientCard(
  api: Api,
  signal: AbortSignal,
  clientId: string,
  commandId: string,
) {
  await api.clients.byClientId(clientId).restore.post({ commandId }, apiOptions(signal));
}

export async function getClientNotes(api: Api, signal: AbortSignal, clientId: string) {
  const client = api.clients.byClientId(clientId);
  const [current, history] = await Promise.all([
    client.notes.get(apiOptions(signal)),
    client.notes.history.get(apiOptions(signal)),
  ]);
  if (!current || !history || !Array.isArray(history.items)) throw invalidResponse();
  const notes = nullableText(current.notes);
  const items: ClientNoteHistoryEntry[] = history.items.map((item: unknown) => {
    if (!isObject(item)) throw invalidResponse();
    const changedAt =
      item.changedAt instanceof Date ? item.changedAt.toISOString() : text(item.changedAt);
    if (!Number.isFinite(Date.parse(changedAt))) throw invalidResponse();
    return {
      id: text(item.id),
      authorName: text(item.authorName),
      changedAt,
      before: nullableText(item.before),
      after: nullableText(item.after),
    };
  });
  return { notes, history: items };
}

export async function updateClientNotes(
  api: Api,
  signal: AbortSignal,
  clientId: string,
  notes: string | null,
  expectedNotes: string | null,
  commandId: string,
) {
  await api.clients
    .byClientId(clientId)
    .notes.put({ commandId, notes, expectedState: { notes: expectedNotes } }, apiOptions(signal));
}
