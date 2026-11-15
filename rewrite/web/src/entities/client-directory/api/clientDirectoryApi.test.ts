import { afterEach, describe, expect, it, vi } from "vitest";
import { applicationApi } from "@/shared/api";
import { initialDirectoryQuery } from "../model/clientDirectory";
import { getClientDirectory, getClientDirectoryOptions } from "./clientDirectoryApi";

const clientId = "019a2c63-8c00-7000-8000-000000000001";
const kindId = "019a2c63-8c00-7000-8000-000000000002";
const contactId = "019a2c63-8c00-7000-8000-000000000003";

function pageResponse() {
  return {
    items: [
      {
        id: clientId,
        firstName: "Анна",
        lastName: "Учебная",
        middleName: "",
        birthdate: "2001-05-06",
        sourceId: null,
        sourceName: null,
        isArchived: false,
        createdAt: "2026-10-10T22:00:00+00:00",
        contacts: [
          {
            id: contactId,
            kindId,
            kindName: "Почта",
            format: "email",
            value: "client@example.test",
            comment: "",
            isPrimary: true,
          },
        ],
      },
    ],
    page: 1,
    pageSize: 10,
    totalCount: 1,
    totalPages: 1,
  };
}

function respondWith(value: unknown) {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockResolvedValue(
      new Response(JSON.stringify(value), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    ),
  );
}

afterEach(() => vi.unstubAllGlobals());

describe("client directory API decoding", () => {
  it("reads a successful JSON response through Kiota with empty optional text", async () => {
    respondWith(pageResponse());

    const result = await getClientDirectory(
      applicationApi(),
      new AbortController().signal,
      initialDirectoryQuery,
    );

    expect(result).toMatchObject({
      totalCount: 1,
      items: [
        {
          id: clientId,
          middleName: "",
          birthdate: "2001-05-06",
          sourceId: null,
          sourceName: null,
          isArchived: false,
          createdAt: "2026-10-10T22:00:00.000Z",
          contacts: [{ id: contactId, format: "email", comment: "", isPrimary: true }],
        },
      ],
    });
  });

  it("reads an empty successful page without treating it as a decoding failure", async () => {
    respondWith({ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 });

    const result = await getClientDirectory(
      applicationApi(),
      new AbortController().signal,
      initialDirectoryQuery,
    );

    expect(result.items).toEqual([]);
    expect(result.totalCount).toBe(0);
  });

  it("reads reference formats and archived flags through Kiota", async () => {
    respondWith({
      sources: [],
      contactKinds: [{ id: kindId, name: "Почта", format: "email", isArchived: false }],
    });

    const result = await getClientDirectoryOptions(applicationApi(), new AbortController().signal);

    expect(result).toEqual({
      sources: [],
      contactKinds: [{ id: kindId, name: "Почта", format: "email", isArchived: false }],
    });
  });

  it("identifies a rejected schema field without exposing the response value", async () => {
    const response = pageResponse();
    const contact = response.items[0]?.contacts[0];
    if (!contact) throw new Error("Fixture is incomplete.");
    contact.format = "private-response-value";
    respondWith(response);

    const result = await getClientDirectory(
      applicationApi(),
      new AbortController().signal,
      initialDirectoryQuery,
    ).catch((error: unknown) => error);

    expect(result).toBeInstanceOf(Error);
    expect((result as Error).message).toContain("clients.items[].contacts[].format");
    expect((result as Error).message).not.toContain("private-response-value");
  });
});
