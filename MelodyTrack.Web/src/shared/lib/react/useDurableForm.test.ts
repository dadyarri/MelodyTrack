import "fake-indexeddb/auto";

import { act, renderHook, waitFor } from "@testing-library/react";
import { Form } from "antd";
import * as v from "valibot";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { melodyTrackDatabase } from "@/shared/database";
import { configureDraftOwner } from "@/shared/lib/storage";
import * as draftStorage from "@/shared/lib/storage/drafts";

import { jsonDurableFormCodec, useDurableForm } from "./useDurableForm";
import { useUnsavedDraftGuard } from "./useUnsavedDraftGuard";

vi.mock("./useUnsavedDraftGuard", () => ({ useUnsavedDraftGuard: vi.fn() }));

type Values = { name?: string };
const key = "draft:durable-form:test";
const schema = v.object({ name: v.optional(v.string()) });
const codec = jsonDurableFormCodec<Values>();

function renderDurableForm() {
  return renderHook(() => {
    const [form] = Form.useForm<Values>();
    return {
      form,
      draft: useDurableForm({ key, schema, form, codec, debounceMs: 10 }),
    };
  });
}

beforeEach(async () => {
  configureDraftOwner(() => "user-1");
  await melodyTrackDatabase.table("drafts").clear();
});

afterEach(async () => {
  vi.restoreAllMocks();
  await melodyTrackDatabase.table("drafts").clear();
});

describe("useDurableForm", () => {
  it("hydrates a valid persisted draft without writing it back as an edit", async () => {
    await draftStorage.saveDraftValues(key, { name: "Recovered" });
    const saveSpy = vi.spyOn(draftStorage, "saveDraftValues");
    const { result } = renderDurableForm();

    await waitFor(() => {
      expect(result.current.draft.status).toBe("restored");
    });

    expect(result.current.form.getFieldValue("name")).toBe("Recovered");
    expect(saveSpy).not.toHaveBeenCalled();
  });

  it("does not silently apply an edit draft from an older server baseline", async () => {
    await draftStorage.saveDraftValues(key, { name: "Older edit" }, { entityId: "entity-1", baselineVersion: "version-1" });
    const { result } = renderHook(() => {
      const [form] = Form.useForm<Values>();
      return {
        form,
        draft: useDurableForm({
          key,
          schema,
          form,
          codec,
          entity: { id: "entity-1", baselineVersion: "version-2" },
        }),
      };
    });

    await waitFor(() => {
      expect(result.current.draft.isStale).toBe(true);
    });
    expect(result.current.form.getFieldValue("name")).toBeUndefined();

    act(() => {
      result.current.draft.reapply();
    });
    expect(result.current.form.getFieldValue("name")).toBe("Older edit");
  });

  it("serializes writes so a slow older write cannot overwrite newer values", async () => {
    const originalSave = draftStorage.saveDraftValues;
    let releaseFirstWrite: (() => void) | undefined;
    const firstWriteBlocked = new Promise<void>((resolve) => {
      releaseFirstWrite = resolve;
    });
    const saveSpy = vi.spyOn(draftStorage, "saveDraftValues").mockImplementationOnce(async (...args) => {
      await firstWriteBlocked;
      return originalSave(...args);
    });
    const { result } = renderDurableForm();
    await waitFor(() => {
      expect(result.current.draft.status).toBe("saved");
    });

    act(() => result.current.draft.formProps.onValuesChange?.({}, { name: "First" }));
    await waitFor(() => {
      expect(saveSpy).toHaveBeenCalledTimes(1);
    });
    act(() => result.current.draft.formProps.onValuesChange?.({}, { name: "Second" }));
    releaseFirstWrite?.();

    await waitFor(() => {
      expect(result.current.draft.status).toBe("saved");
    });
    expect((await draftStorage.loadDraft(key, schema))?.values).toEqual({ name: "Second" });
  });

  it("keeps failed values available for an explicit retry", async () => {
    const originalSave = draftStorage.saveDraftValues;
    vi.spyOn(draftStorage, "saveDraftValues").mockRejectedValueOnce(new Error("quota")).mockImplementation(originalSave);
    const { result } = renderDurableForm();
    await waitFor(() => {
      expect(result.current.draft.status).toBe("saved");
    });

    act(() => result.current.draft.formProps.onValuesChange?.({}, { name: "Retry me" }));
    await waitFor(() => {
      expect(result.current.draft.status).toBe("failed");
    });
    act(() => {
      result.current.draft.retry();
    });
    await waitFor(() => {
      expect(result.current.draft.status).toBe("saved");
    });

    expect((await draftStorage.loadDraft(key, schema))?.values).toEqual({ name: "Retry me" });
  });

  it("clears a draft only when explicitly requested", async () => {
    const { result } = renderDurableForm();
    await waitFor(() => {
      expect(result.current.draft.status).toBe("saved");
    });
    act(() => result.current.draft.formProps.onValuesChange?.({}, { name: "Keep me" }));
    await waitFor(() => {
      expect(result.current.draft.status).toBe("saved");
    });

    await act(() => result.current.draft.clearAfterSuccess());

    expect(result.current.draft.hasDraft).toBe(false);
    expect(await draftStorage.loadDraft(key, schema)).toBeNull();
  });

  it("restores values after a modal-style close and reopen", async () => {
    const { result, rerender } = renderHook(
      ({ enabled }: { enabled: boolean }) => {
        const [form] = Form.useForm<Values>();
        return { form, draft: useDurableForm({ key, schema, form, codec, enabled, debounceMs: 10 }) };
      },
      { initialProps: { enabled: true } },
    );
    act(() => result.current.draft.formProps.onValuesChange?.({}, { name: "Survives close" }));
    rerender({ enabled: false });
    expect(vi.mocked(useUnsavedDraftGuard)).toHaveBeenLastCalledWith(true, "saving");

    await waitFor(async () => {
      expect((await draftStorage.loadDraft(key, schema))?.values).toEqual({ name: "Survives close" });
    });
    act(() => {
      result.current.form.resetFields();
    });
    rerender({ enabled: true });

    await waitFor(() => {
      expect(result.current.draft.status).toBe("restored");
    });
    expect(result.current.form.getFieldValue("name")).toBe("Survives close");
  });
});
