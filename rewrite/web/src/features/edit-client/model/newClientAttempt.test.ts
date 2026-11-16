import { describe, expect, it, vi } from "vitest";
import type { ClientCardDraft } from "../ui/ClientEditorDrawer";
import { acknowledgeNewClientAttempt, prepareNewClientAttempt } from "./newClientAttempt";

const draft: ClientCardDraft = {
  firstName: "Имя",
  lastName: "Фамилия",
  middleName: null,
  birthdate: null,
  sourceId: "source",
  contacts: [],
};
function storage() {
  const records = new Map<string, unknown>();
  return {
    records,
    get: vi.fn(async (id: string) => records.get(id)),
    put: vi.fn(async (id: string, value: unknown, _kind: string) => {
      records.set(id, value);
    }),
    remove: vi.fn(async (id: string) => {
      records.delete(id);
    }),
  };
}
describe("durable client creation", () => {
  it("reuses the same identifiers and intent after the sending page restarts", async () => {
    const store = storage();
    const first = await prepareNewClientAttempt(store, draft);

    const restored = await prepareNewClientAttempt(store, structuredClone(draft));

    expect(restored.commandId).toBe(first.commandId);
    expect(restored.clientId).toBe(first.clientId);
    expect(restored.draft).toEqual(draft);
    expect(store.put).toHaveBeenCalledTimes(1);
  });
  it("preserves an unresolved attempt when a different intent tries to replace it", async () => {
    const store = storage();
    const first = await prepareNewClientAttempt(store, draft);

    await expect(
      prepareNewClientAttempt(store, { ...draft, firstName: "Другое" }),
    ).rejects.toThrow();

    expect(await store.get("new-client-command")).toEqual({ schemaVersion: 1, ...first });
    expect(store.put).toHaveBeenCalledTimes(1);
  });
  it("rejects an unknown saved version without overwriting it", async () => {
    const store = storage();
    store.records.set("new-client-command", { schemaVersion: 2, futureData: true });

    await expect(prepareNewClientAttempt(store, draft)).rejects.toThrow();

    expect(store.put).not.toHaveBeenCalled();
  });
  it("keeps the idempotency key if writing the applied receipt fails", async () => {
    const store = storage();
    const command = await prepareNewClientAttempt(store, draft);
    store.put.mockRejectedValueOnce(new Error("storage unavailable"));

    await expect(acknowledgeNewClientAttempt(store, command)).rejects.toThrow();

    expect(store.records.has("new-client-command")).toBe(true);
    expect(store.remove).not.toHaveBeenCalled();
  });
  it("removes the form backup before releasing the confirmed pending command", async () => {
    const store = storage();
    const command = await prepareNewClientAttempt(store, draft);
    store.records.set("new-client-draft", draft);

    await acknowledgeNewClientAttempt(store, command);

    expect(store.records.has("new-client-command")).toBe(false);
    expect(store.records.has("new-client-draft")).toBe(false);
    expect(store.records.has(`client-create:${command.commandId}`)).toBe(true);
    expect(store.remove.mock.calls).toEqual([["new-client-draft"], ["new-client-command"]]);
  });
});
