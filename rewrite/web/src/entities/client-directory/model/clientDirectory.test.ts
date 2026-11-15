import { DateOnly } from "@microsoft/kiota-abstractions";
import { describe, expect, it } from "vitest";
import { readDirectoryPage } from "./clientDirectory";

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
});
