import "fake-indexeddb/auto";

import * as v from "valibot";
import { afterEach, beforeEach, describe, expect, it } from "vitest";

import { melodyTrackDatabase } from "@/shared/database";

import { loadDraft, saveDraftValues } from "./drafts";
import { configureDraftOwner } from "./owner";

const key = "draft:test:create";
const testDraftSchema = v.object({ name: v.optional(v.string()) });
let ownerUserId = "user-1";

beforeEach(async () => {
  ownerUserId = "user-1";
  configureDraftOwner(() => ownerUserId);
  localStorage.clear();
  await melodyTrackDatabase.table("drafts").clear();
});

afterEach(async () => {
  await melodyTrackDatabase.table("drafts").clear();
  localStorage.clear();
});

describe("draft persistence", () => {
  it("partitions drafts by authenticated user", async () => {
    await saveDraftValues(key, { name: "First user" });

    ownerUserId = "user-2";
    expect(await loadDraft(key, testDraftSchema)).toBeNull();
    await saveDraftValues(key, { name: "Second user" });

    ownerUserId = "user-1";
    expect(await loadDraft(key, testDraftSchema)).toMatchObject({ values: { name: "First user" } });
  });

  it("migrates a valid legacy draft into the current user partition", async () => {
    localStorage.setItem(
      key,
      JSON.stringify({
        replayKey: "legacy-replay",
        updatedAtUtc: new Date().toISOString(),
        values: { name: "Recovered" },
      }),
    );

    expect(await loadDraft(key, testDraftSchema)).toMatchObject({ values: { name: "Recovered" } });
    expect(localStorage.getItem(key)).toBeNull();
    expect(await melodyTrackDatabase.table("drafts").count()).toBe(1);
  });

  it("discards invalid and expired drafts at the read boundary", async () => {
    localStorage.setItem(
      key,
      JSON.stringify({
        replayKey: "invalid",
        updatedAtUtc: new Date().toISOString(),
        values: { name: 42 },
      }),
    );
    expect(await loadDraft(key, testDraftSchema)).toBeNull();

    await saveDraftValues(key, { name: "Old" });
    await melodyTrackDatabase.table("drafts").where({ ownerUserId, key }).modify({ expiresAtUtc: "2020-01-01T00:00:00.000Z" });
    expect(await loadDraft(key, testDraftSchema)).toBeNull();
  });
});
