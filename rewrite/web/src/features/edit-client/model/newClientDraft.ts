import type { ClientCardDraft, ClientContactDraft } from "../ui/ClientEditorDrawer";

const draftKeys = [
  "schemaVersion",
  "firstName",
  "lastName",
  "middleName",
  "birthdate",
  "sourceId",
  "contacts",
] as const;
const cardKeys = [
  "firstName",
  "lastName",
  "middleName",
  "birthdate",
  "sourceId",
  "contacts",
] as const;
const contactKeys = ["id", "kindId", "value", "comment", "isPrimary"] as const;

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(value).sort();
  const expected = [...keys].sort();
  return actual.length === expected.length && actual.every((key, index) => key === expected[index]);
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === "string";
}

function isContactDraft(value: unknown): value is ClientContactDraft {
  if (typeof value !== "object" || value === null || Array.isArray(value)) return false;
  const contact = value as Record<string, unknown>;
  return (
    hasExactKeys(contact, contactKeys) &&
    typeof contact.id === "string" &&
    typeof contact.kindId === "string" &&
    typeof contact.value === "string" &&
    isNullableString(contact.comment) &&
    typeof contact.isPrimary === "boolean"
  );
}

function isClientCardDraft(value: unknown): value is ClientCardDraft {
  if (typeof value !== "object" || value === null || Array.isArray(value)) return false;
  const draft = value as Record<string, unknown>;
  return (
    hasExactKeys(draft, cardKeys) &&
    typeof draft.firstName === "string" &&
    typeof draft.lastName === "string" &&
    isNullableString(draft.middleName) &&
    isNullableString(draft.birthdate) &&
    typeof draft.sourceId === "string" &&
    Array.isArray(draft.contacts) &&
    draft.contacts.every(isContactDraft)
  );
}

export function isNewClientDraft(value: unknown): value is ClientCardDraft & { schemaVersion: 1 } {
  if (typeof value !== "object" || value === null || Array.isArray(value)) return false;
  const draft = value as Record<string, unknown>;
  return (
    hasExactKeys(draft, draftKeys) &&
    draft.schemaVersion === 1 &&
    isClientCardDraft({
      firstName: draft.firstName,
      lastName: draft.lastName,
      middleName: draft.middleName,
      birthdate: draft.birthdate,
      sourceId: draft.sourceId,
      contacts: draft.contacts,
    })
  );
}

export function toStoredNewClientDraft(draft: ClientCardDraft): unknown {
  return { schemaVersion: 1, ...draft };
}

export type NewClientCommandDraft = {
  commandId: string;
  clientId: string;
  draft: ClientCardDraft;
};

const commandKeys = ["schemaVersion", "commandId", "clientId", "draft"] as const;
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export function isNewClientCommand(value: unknown): value is NewClientCommandDraft {
  if (typeof value !== "object" || value === null || Array.isArray(value)) return false;
  const command = value as Record<string, unknown>;
  return (
    hasExactKeys(command, commandKeys) &&
    command.schemaVersion === 1 &&
    typeof command.commandId === "string" &&
    uuidPattern.test(command.commandId) &&
    typeof command.clientId === "string" &&
    uuidPattern.test(command.clientId) &&
    isClientCardDraft(command.draft)
  );
}
