import { Form, Input } from "antd";
import * as v from "valibot";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render } from "vitest-browser-react";

import { melodyTrackDatabase } from "@/shared/database";
import { configureDraftOwner } from "@/shared/lib/storage";

import { loadDraft, saveDraftValues } from "../storage/drafts";
import { jsonDurableFormCodec, useDurableForm } from "./useDurableForm";

vi.mock("./useUnsavedDraftGuard", () => ({ useUnsavedDraftGuard: vi.fn() }));

type Values = { name?: string };
const schema = v.object({ name: v.optional(v.string()) });
const codec = jsonDurableFormCodec<Values>();
let ownerUserId = "browser-user-1";

function DraftHarness({
  draftKey,
  serverValue,
  entity,
}: {
  draftKey: string;
  serverValue?: string;
  entity?: { id: string; baselineVersion: string };
}) {
  const [form] = Form.useForm<Values>();
  const draft = useDurableForm({ key: draftKey, schema, form, codec, entity, debounceMs: 20 });

  return (
    <Form form={form} initialValues={{ name: serverValue }} onValuesChange={draft.formProps.onValuesChange}>
      <output data-testid="draft-status">{draft.status}</output>
      {draft.isStale ? (
        <button type="button" onClick={draft.reapply}>
          Reapply draft
        </button>
      ) : null}
      <Form.Item name="name" label="Name">
        <Input />
      </Form.Item>
    </Form>
  );
}

async function expectStoredValue(draftKey: string, name: string) {
  await expect.poll(async () => (await loadDraft(draftKey, schema))?.values.name).toBe(name);
}

beforeEach(async () => {
  ownerUserId = "browser-user-1";
  configureDraftOwner(() => ownerUserId);
  await melodyTrackDatabase.table("drafts").clear();
});

afterEach(async () => {
  await melodyTrackDatabase.table("drafts").clear();
});

describe("durable forms in a browser", () => {
  it("restores a create form after remount without crossing accounts", async () => {
    const draftKey = "draft:browser:create";
    const first = await render(<DraftHarness draftKey={draftKey} />);
    await first.getByLabelText("Name").fill("First account draft");
    await expectStoredValue(draftKey, "First account draft");
    await first.unmount();

    ownerUserId = "browser-user-2";
    const second = await render(<DraftHarness draftKey={draftKey} />);
    await expect.element(second.getByLabelText("Name")).toHaveValue("");
    await second.getByLabelText("Name").fill("Second account draft");
    await expectStoredValue(draftKey, "Second account draft");
    await second.unmount();

    ownerUserId = "browser-user-1";
    const restored = await render(<DraftHarness draftKey={draftKey} />);
    await expect.element(restored.getByLabelText("Name")).toHaveValue("First account draft");
  });

  it("restores an entity-scoped edit draft after remount", async () => {
    const draftKey = "draft:browser:client:client-1";
    const entity = { id: "client-1", baselineVersion: "version-1" };
    const first = await render(<DraftHarness draftKey={draftKey} serverValue="Server value" entity={entity} />);
    await first.getByLabelText("Name").fill("Edited draft");
    await expectStoredValue(draftKey, "Edited draft");
    await first.unmount();

    const restored = await render(<DraftHarness draftKey={draftKey} serverValue="Server value" entity={entity} />);
    await expect.element(restored.getByLabelText("Name")).toHaveValue("Edited draft");
  });

  it("requires deliberate reapplication when the server baseline changed", async () => {
    const draftKey = "draft:browser:client:client-2";
    await saveDraftValues(draftKey, { name: "Older draft" }, { entityId: "client-2", baselineVersion: "version-1" });

    const screen = await render(
      <DraftHarness draftKey={draftKey} serverValue="New server value" entity={{ id: "client-2", baselineVersion: "version-2" }} />,
    );
    await expect.element(screen.getByLabelText("Name")).toHaveValue("New server value");
    await screen.getByRole("button", { name: "Reapply draft" }).click();
    await expect.element(screen.getByLabelText("Name")).toHaveValue("Older draft");
  });
});
