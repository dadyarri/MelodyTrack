import { describe, expect, it } from "vitest";
import { isNewClientCommand, isNewClientDraft, toStoredNewClientDraft } from "./newClientDraft";

describe("new client draft validation", () => {
  it("accepts only the versioned card shape that the editor can restore", () => {
    const stored = toStoredNewClientDraft({
      firstName: "Анна",
      lastName: "Иванова",
      middleName: null,
      birthdate: "2001-05-06",
      sourceId: "source",
      contacts: [
        {
          id: "contact",
          kindId: "phone",
          value: "+7 900 000-00-01",
          comment: null,
          isPrimary: true,
        },
      ],
    });

    expect(isNewClientDraft(stored)).toBe(true);
  });

  it("rejects future versions and unexpected fields without normalizing them", () => {
    const draft = {
      schemaVersion: 2,
      firstName: "Анна",
      lastName: "Иванова",
      middleName: null,
      birthdate: null,
      sourceId: "source",
      contacts: [],
      serverState: "must not be interpreted",
    };

    expect(isNewClientDraft(draft)).toBe(false);
  });

  it("rejects malformed contacts rather than partially restoring the card", () => {
    expect(
      isNewClientDraft({
        schemaVersion: 1,
        firstName: "Анна",
        lastName: "Иванова",
        middleName: null,
        birthdate: null,
        sourceId: "source",
        contacts: [{ id: "contact", kindId: "phone", value: 4, comment: null, isPrimary: true }],
      }),
    ).toBe(false);
  });

  it("accepts only a retry command with stable UUIDs and a strict card payload", () => {
    const command = {
      schemaVersion: 1,
      commandId: "0192f0e0-1234-7abc-8def-123456789012",
      clientId: "0192f0e0-1234-7abc-8def-123456789013",
      draft: {
        firstName: "Анна",
        lastName: "Иванова",
        middleName: null,
        birthdate: null,
        sourceId: "source",
        contacts: [],
      },
    };

    expect(isNewClientCommand(command)).toBe(true);
    expect(isNewClientCommand({ ...command, commandId: "new-id" })).toBe(false);
  });
});
