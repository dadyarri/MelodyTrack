import type { FormInstance, FormProps } from "antd";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import * as v from "valibot";

import { clearDraft, type DraftMetadata, loadDraft, saveDraftValues, withDraftHydration } from "../storage/drafts";
import { getStorageOwnerUserId } from "../storage/owner";
import { useUnsavedDraftGuard } from "./useUnsavedDraftGuard";

export type DurableFormStatus = "loading" | "restored" | "saving" | "saved" | "failed";

export type DurableFormCodec<TValues, TPersisted> = {
  serialize: (values: TValues) => TPersisted;
  deserialize: (values: TPersisted) => Partial<TValues>;
};

export const jsonDurableFormCodec = <TValues>() => ({
  serialize: (values: TValues) => values,
  deserialize: (values: TValues) => values,
});

type DurableEntity = {
  id: string;
  baselineVersion?: string | null;
};

type UseDurableFormOptions<TValues, TPersisted> = {
  key: string | null;
  schema: v.GenericSchema<unknown, TPersisted>;
  form: FormInstance<TValues>;
  codec: DurableFormCodec<TValues, TPersisted>;
  enabled?: boolean;
  entity?: DurableEntity;
  debounceMs?: number;
  onRestore?: (values: TPersisted) => void;
};

export function useDurableForm<TValues, TPersisted>({
  key,
  schema,
  form,
  codec,
  enabled = true,
  entity,
  debounceMs = 400,
  onRestore,
}: UseDurableFormOptions<TValues, TPersisted>) {
  const [status, setStatus] = useState<DurableFormStatus>("loading");
  const [hasDraft, setHasDraft] = useState(false);
  const [isStale, setStale] = useState(false);
  const hydrationRef = useRef(false);
  const loadedValuesRef = useRef<TPersisted | null>(null);
  const pendingWriteRef = useRef<{
    key: string;
    ownerUserId: string;
    values: TPersisted;
    metadata: DraftMetadata;
    revision: number;
  } | null>(null);
  const timerRef = useRef<number | null>(null);
  const revisionRef = useRef(0);
  const queueRef = useRef<Promise<unknown>>(Promise.resolve());
  const keyRef = useRef(key);
  const metadataRef = useRef<DraftMetadata>({ entityId: entity?.id, baselineVersion: entity?.baselineVersion });
  const codecRef = useRef(codec);
  const onRestoreRef = useRef(onRestore);
  const ownerUserId = getStorageOwnerUserId();

  useEffect(() => {
    keyRef.current = key;
    metadataRef.current = { entityId: entity?.id, baselineVersion: entity?.baselineVersion };
    codecRef.current = codec;
    onRestoreRef.current = onRestore;
  }, [codec, entity?.baselineVersion, entity?.id, key, onRestore]);

  const applyPersistedValues = useCallback(
    (values: TPersisted) => {
      withDraftHydration(hydrationRef, () => {
        form.setFieldsValue(codecRef.current.deserialize(values) as Parameters<FormInstance<TValues>["setFieldsValue"]>[0]);
        onRestoreRef.current?.(values);
      });
    },
    [form],
  );

  useEffect(() => {
    if (!enabled || !key || !ownerUserId) {
      return;
    }

    let active = true;
    revisionRef.current += 1;
    const loadRevision = revisionRef.current;
    queueMicrotask(() => {
      if (active && loadRevision === revisionRef.current) setStatus("loading");
    });
    void loadDraft(key, schema, ownerUserId)
      .then((draft) => {
        if (!active || loadRevision !== revisionRef.current) {
          return;
        }

        loadedValuesRef.current = draft?.values ?? null;
        setHasDraft(draft !== null);
        const stale = Boolean(
          draft?.entityId && entity?.id && (draft.entityId !== entity.id || draft.baselineVersion !== (entity.baselineVersion ?? null)),
        );
        setStale(stale);
        if (draft && !stale) {
          applyPersistedValues(draft.values);
          setStatus("restored");
        } else {
          setStatus("saved");
        }
      })
      .catch(() => {
        if (active && loadRevision === revisionRef.current) {
          setStatus("failed");
        }
      });

    return () => {
      active = false;
    };
  }, [applyPersistedValues, enabled, entity?.baselineVersion, entity?.id, key, ownerUserId, schema]);

  const persist = useCallback((values: TPersisted, revision: number, targetKey: string | null, metadata: DraftMetadata, owner: string) => {
    if (!targetKey) {
      return;
    }
    const operation = queueRef.current.catch(() => undefined).then(() => saveDraftValues(targetKey, values, metadata, owner));
    queueRef.current = operation;
    void operation
      .then(() => {
        if (revision === revisionRef.current) {
          if (pendingWriteRef.current?.revision === revision) pendingWriteRef.current = null;
          loadedValuesRef.current = values;
          setHasDraft(true);
          setStale(false);
          setStatus("saved");
        }
      })
      .catch(() => {
        if (revision === revisionRef.current) {
          setStatus("failed");
        }
      });
  }, []);

  const save = useCallback(
    (values: TValues) => {
      if (!enabled || !keyRef.current || hydrationRef.current) {
        return;
      }

      let serialized: TPersisted;
      try {
        serialized = codecRef.current.serialize(values);
        if (!v.safeParse(schema, serialized).success) {
          throw new Error("Draft values failed runtime validation.");
        }
      } catch {
        setStatus("failed");
        return;
      }

      const revision = revisionRef.current + 1;
      revisionRef.current = revision;
      setHasDraft(true);
      setStale(false);
      setStatus("saving");
      const targetKey = keyRef.current;
      if (!targetKey || !ownerUserId) return;
      const metadata = metadataRef.current;
      const previousWrite = pendingWriteRef.current;
      if (previousWrite && previousWrite.key !== targetKey) {
        persist(previousWrite.values, previousWrite.revision, previousWrite.key, previousWrite.metadata, previousWrite.ownerUserId);
      }
      pendingWriteRef.current = { key: targetKey, ownerUserId, values: serialized, metadata, revision };
      if (timerRef.current !== null) {
        window.clearTimeout(timerRef.current);
      }
      timerRef.current = window.setTimeout(() => {
        timerRef.current = null;
        const pendingWrite = pendingWriteRef.current;
        if (pendingWrite?.revision === revision) {
          persist(pendingWrite.values, revision, pendingWrite.key, pendingWrite.metadata, pendingWrite.ownerUserId);
        }
      }, debounceMs);
    },
    [debounceMs, enabled, ownerUserId, persist, schema],
  );

  useEffect(
    () => () => {
      if (timerRef.current !== null) {
        window.clearTimeout(timerRef.current);
      }
      const pendingWrite = pendingWriteRef.current;
      if (pendingWrite) {
        queueRef.current = queueRef.current
          .catch(() => undefined)
          .then(() => saveDraftValues(pendingWrite.key, pendingWrite.values, pendingWrite.metadata, pendingWrite.ownerUserId));
      }
    },
    [],
  );

  const clear = useCallback(async () => {
    const clearOwnerUserId = getStorageOwnerUserId();
    if (!keyRef.current || !clearOwnerUserId) {
      return;
    }
    if (timerRef.current !== null) {
      window.clearTimeout(timerRef.current);
      timerRef.current = null;
    }
    if (pendingWriteRef.current?.key === keyRef.current) pendingWriteRef.current = null;
    loadedValuesRef.current = null;
    revisionRef.current += 1;
    setHasDraft(false);
    setStale(false);
    setStatus("saved");
    const currentKey = keyRef.current;
    const operation = queueRef.current.catch(() => undefined).then(() => clearDraft(currentKey, clearOwnerUserId));
    queueRef.current = operation;
    try {
      await operation;
    } catch {
      setStatus("failed");
    }
  }, []);

  const retry = useCallback(() => {
    const pendingWrite = pendingWriteRef.current;
    if (pendingWrite !== null) {
      const revision = revisionRef.current + 1;
      revisionRef.current = revision;
      setStatus("saving");
      pendingWrite.revision = revision;
      persist(pendingWrite.values, revision, pendingWrite.key, pendingWrite.metadata, pendingWrite.ownerUserId);
      return;
    }
    save(form.getFieldsValue());
  }, [form, persist, save]);

  const reapply = useCallback(() => {
    if (loadedValuesRef.current !== null) {
      applyPersistedValues(loadedValuesRef.current);
      setStale(false);
      setStatus("restored");
    }
  }, [applyPersistedValues]);

  const formProps = useMemo<Pick<FormProps<TValues>, "onValuesChange">>(
    () => ({
      onValuesChange: (_changed, values) => {
        save(values);
      },
    }),
    [save],
  );

  useUnsavedDraftGuard(hasDraft, status);

  return {
    status,
    hasDraft,
    restored: status === "restored",
    isStale,
    isHydratingRef: hydrationRef,
    formProps,
    discard: clear,
    clearAfterSuccess: clear,
    retry,
    reapply,
  };
}
