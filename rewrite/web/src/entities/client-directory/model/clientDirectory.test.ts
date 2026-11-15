import { DateOnly } from "@microsoft/kiota-abstractions";
import { describe, expect, it } from "vitest";
import { readDirectoryOptions, readDirectoryPage } from "./clientDirectory";

describe("client directory response", () => {
  it("keeps an absent source and empty contacts without inventing lifecycle or balance", () => {
    const value = readDirectoryPage({
      items: [
        {
          id: "client-a",
          firstName: "Анна",
          lastName: "Учебная",
          middleName: null,
          sourceId: null,
          sourceName: null,
          birthdate: DateOnly.parse("2001-05-06"),
          createdAt: new Date("2026-10-10T22:00:00Z"),
          isArchived: false,
          contacts: [],
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
      totalPages: 1,
    });

    expect(value.items[0]).toMatchObject({
      sourceName: null,
      contacts: [],
      birthdate: "2001-05-06",
      createdAt: "2026-10-10T22:00:00.000Z",
    });
    expect(value.items[0]).not.toHaveProperty("balance");
    expect(value.items[0]).not.toHaveProperty("lifecycle");
  });

  it("rejects inconsistent pagination instead of presenting a partial result as complete", () => {
    expect(() =>
      readDirectoryPage({ items: [], page: 1, pageSize: 20, totalCount: 40, totalPages: 1 }),
    ).toThrow();
  });

  it("preserves every contact field, nullable comment, source, and DateOnly value", () => {
    const value = readDirectoryPage({
      items: [
        {
          id: "client-a",
          firstName: "Анна",
          lastName: "Тестова",
          middleName: "Ивановна",
          sourceId: "source-a",
          sourceName: "Рекомендация",
          birthdate: DateOnly.parse("2001-05-06"),
          createdAt: new Date("2026-10-10T22:00:00Z"),
          isArchived: true,
          contacts: [
            {
              id: "contact-phone",
              kindId: "kind-phone",
              kindName: "Телефон",
              format: "phone",
              value: "+7 (900) 123-45-67",
              comment: "Для связи утром",
              isPrimary: true,
            },
            {
              id: "contact-email",
              kindId: "kind-email",
              kindName: "Электронная почта",
              format: "email",
              value: "anna@example.test",
              comment: null,
              isPrimary: false,
            },
            {
              id: "contact-link",
              kindId: "kind-link",
              kindName: "Сайт",
              format: "link",
              value: "https://example.test",
              comment: "Профиль",
              isPrimary: false,
            },
          ],
        },
      ],
      page: 1,
      pageSize: 10,
      totalCount: 1,
      totalPages: 1,
    });

    expect(value.items[0]).toMatchObject({
      id: "client-a",
      sourceId: "source-a",
      sourceName: "Рекомендация",
      birthdate: "2001-05-06",
      createdAt: "2026-10-10T22:00:00.000Z",
      isArchived: true,
      contacts: [
        {
          id: "contact-phone",
          kindId: "kind-phone",
          kindName: "Телефон",
          format: "phone",
          value: "+7 (900) 123-45-67",
          comment: "Для связи утром",
          isPrimary: true,
        },
        {
          id: "contact-email",
          kindId: "kind-email",
          kindName: "Электронная почта",
          format: "email",
          value: "anna@example.test",
          comment: null,
          isPrimary: false,
        },
        {
          id: "contact-link",
          kindId: "kind-link",
          kindName: "Сайт",
          format: "link",
          value: "https://example.test",
          comment: "Профиль",
          isPrimary: false,
        },
      ],
    });
  });

  it("rejects invalid contact formats and missing primary flags", () => {
    const base = {
      page: 1,
      pageSize: 10,
      totalCount: 1,
      totalPages: 1,
      items: [
        {
          id: "client-a",
          firstName: "Анна",
          lastName: "Тестова",
          birthdate: null,
          createdAt: "2026-10-10T22:00:00Z",
          isArchived: false,
          contacts: [
            {
              id: "contact-a",
              kindId: "kind-a",
              kindName: "Телефон",
              format: "fax",
              value: "123",
              comment: null,
              isPrimary: true,
            },
          ],
        },
      ],
    };
    expect(() => readDirectoryPage(base)).toThrow();

    const contact = base.items[0]?.contacts[0];
    if (!contact) throw new Error("Fixture is incomplete.");
    expect(() =>
      readDirectoryPage({
        ...base,
        items: [
          {
            ...base.items[0],
            contacts: [{ ...contact, format: "phone", isPrimary: undefined }],
          },
        ],
      }),
    ).toThrow();
  });

  it("maps source options and rejects an invalid reference row", () => {
    expect(readDirectoryOptions({ sources: [{ id: "source-a", name: "Рекомендация" }] })).toEqual({
      sources: [{ id: "source-a", name: "Рекомендация" }],
    });
    expect(() => readDirectoryOptions({ sources: [{ id: "source-a", name: "" }] })).toThrow();
  });
});
