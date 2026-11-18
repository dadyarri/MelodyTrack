import { useEffect, useId, useLayoutEffect, useRef, useState } from "react";
import type { DirectoryClient } from "@/entities/client-directory";
import { useOfflineStorage } from "@/entities/offline-storage";
import { uuidv7 } from "@/shared/lib/uuidv7";
import { DateInput } from "@/shared/ui/date-time";
import { Icon } from "@/shared/ui/Icon";
import { useErrorNotification } from "@/shared/ui/notifications";
import { PaperButton, PaperDrawer, PaperInput, PaperSelect, PaperTooltip } from "@/shared/ui/paper";
import {
  isNewClientCommand,
  isNewClientDraft,
  type NewClientCommandDraft,
  toStoredNewClientDraft,
} from "../model/newClientDraft";
import styles from "./client-editor.module.css";

export type ClientContactKind = {
  id: string;
  name: string;
  format: "phone" | "email" | "link";
  isArchived: boolean;
};

export type ClientContactDraft = {
  id: string;
  kindId: string;
  value: string;
  comment: string | null;
  isPrimary: boolean;
};

export type ClientCardDraft = {
  firstName: string;
  lastName: string;
  middleName: string | null;
  birthdate: string | null;
  sourceId: string;
  contacts: ClientContactDraft[];
};

type ClientEditorDrawerProps = {
  open: boolean;
  client: DirectoryClient | null;
  sourceOptions: { id: string; name: string }[];
  contactKinds: ClientContactKind[];
  loading?: boolean;
  saving?: boolean;
  error?: string | null;
  stale?: boolean;
  hasDuplicateContacts?: boolean;
  resetKey?: string | number;
  persistNewClientDraft?: boolean;
  draftSavedKey?: number;
  unconfirmedCreate?: boolean;
  onClose: () => void;
  onSave: (draft: ClientCardDraft) => void;
  onReload?: () => void;
};

function draftFromClient(client: DirectoryClient | null): ClientCardDraft {
  return {
    firstName: client?.firstName ?? "",
    lastName: client?.lastName ?? "",
    middleName: client?.middleName ?? "",
    birthdate: client?.birthdate ?? "",
    sourceId: client?.sourceId ?? "",
    contacts:
      client?.contacts.map((contact) => ({
        id: contact.id,
        kindId: contact.kindId,
        value: contact.value,
        comment: contact.comment,
        isPrimary: contact.isPrimary,
      })) ?? [],
  };
}

type ValidationIssue = { message: string; fields: string[] };

function validationError(draft: ClientCardDraft): ValidationIssue | null {
  const missing = [
    ...(!draft.lastName.trim() ? ["lastName"] : []),
    ...(!draft.firstName.trim() ? ["firstName"] : []),
    ...(!draft.sourceId ? ["sourceId"] : []),
  ];
  if (missing.length)
    return { message: "Заполните фамилию, имя и источник клиента.", fields: missing };
  const contactFields = draft.contacts.flatMap((contact) => [
    ...(!contact.kindId ? [`kind-${contact.id}`] : []),
    ...(!contact.value.trim() ? [`value-${contact.id}`] : []),
  ]);
  if (contactFields.length)
    return {
      message: "Для каждого контакта выберите вид и укажите значение.",
      fields: contactFields,
    };
  const kinds = new Set(draft.contacts.map((contact) => contact.kindId));
  for (const kindId of kinds) {
    if (
      draft.contacts.filter((contact) => contact.kindId === kindId && contact.isPrimary).length !==
      1
    )
      return {
        message: "Для каждого вида контакта отметьте ровно один основной контакт.",
        fields: draft.contacts
          .filter((contact) => contact.kindId === kindId)
          .map((contact) => `primary-${contact.id}`),
      };
  }
  return null;
}

export function ClientEditorDrawer({
  open,
  client,
  sourceOptions,
  contactKinds,
  loading = false,
  saving = false,
  error = null,
  stale = false,
  hasDuplicateContacts = false,
  resetKey = 0,
  persistNewClientDraft = false,
  draftSavedKey = 0,
  unconfirmedCreate = false,
  onClose,
  onSave,
  onReload,
}: ClientEditorDrawerProps) {
  const [draft, setDraft] = useState(() => draftFromClient(client));
  const [validation, setValidation] = useState<ValidationIssue | null>(null);
  const formId = useId();
  const form = useRef<HTMLFormElement>(null);
  const clientId = client?.id;
  const latestClient = useRef(client);
  const originalDraft = useRef(draftFromClient(client));
  const previousSession = useRef({ open: false, clientId: client?.id ?? null, resetKey });
  const offlineStorage = useOfflineStorage();
  const offlineStorageGet = offlineStorage?.get;
  const offlineStorageStatus = offlineStorage?.status;
  const offlineStorageStaffId = offlineStorage?.staffId;
  const restoreGeneration = useRef(0);
  const typedGeneration = useRef(0);
  const saveQueue = useRef(Promise.resolve());
  const removeTask = useRef(Promise.resolve());
  const [draftReady, setDraftReady] = useState(false);
  const [draftPresent, setDraftPresent] = useState(false);
  const [draftError, setDraftError] = useState<string | null>(null);
  const [draftSaving, setDraftSaving] = useState(false);
  const [draftRemoving, setDraftRemoving] = useState(false);
  const [commandCheckPending, setCommandCheckPending] = useState(false);
  const [pendingCommand, setPendingCommand] = useState<NewClientCommandDraft | null>(null);
  const [pendingDelivery, setPendingDelivery] = useState(unconfirmedCreate);
  const [pendingCommandRestored, setPendingCommandRestored] = useState(false);
  const [draftFormatUnknown, setDraftFormatUnknown] = useState(false);
  const allowDraftRestore = useRef(true);
  const pendingSaveCount = useRef(0);
  const closeWaitCancels = useRef(new Set<() => void>());
  const offlineStorageRef = useRef(offlineStorage);
  const unconfirmedWasSet = useRef(unconfirmedCreate);
  const pendingDeliveryRef = useRef(pendingDelivery);
  const pendingCommandRestoredRef = useRef(pendingCommandRestored);
  const unconfirmedCreateRef = useRef(unconfirmedCreate);
  const observedStaffId = useRef<string | null>(offlineStorage?.staffId ?? null);
  offlineStorageRef.current = offlineStorage;
  pendingDeliveryRef.current = pendingDelivery;
  pendingCommandRestoredRef.current = pendingCommandRestored;
  unconfirmedCreateRef.current = unconfirmedCreate;
  latestClient.current = client;

  function isDraftSubmitAllowed() {
    if (!persistNewClientDraft || client !== null || !open) return true;
    const storage = offlineStorageRef.current;
    return storage === null || storage.status === "unlocked";
  }

  const vaultAccessAllowed =
    !persistNewClientDraft ||
    client !== null ||
    !open ||
    offlineStorage === null ||
    offlineStorage.status === "unlocked";

  const canPersistDraft =
    persistNewClientDraft &&
    client === null &&
    open &&
    offlineStorage?.status === "unlocked" &&
    offlineStorage.staffId !== null;

  useLayoutEffect(() => {
    if (!persistNewClientDraft || client !== null || !open || !offlineStorageStatus) return;
    const staffChanged =
      observedStaffId.current !== null &&
      offlineStorageStaffId !== null &&
      observedStaffId.current !== offlineStorageStaffId;
    observedStaffId.current = offlineStorageStaffId ?? null;
    if (!staffChanged && offlineStorageStatus === "unlocked") return;

    ++restoreGeneration.current;
    for (const cancel of closeWaitCancels.current) cancel();
    closeWaitCancels.current.clear();
    allowDraftRestore.current = true;
    typedGeneration.current = 0;
    saveQueue.current = Promise.resolve();
    removeTask.current = Promise.resolve();
    originalDraft.current = draftFromClient(null);
    setDraft(draftFromClient(null));
    setValidation(null);
    setDraftReady(false);
    setDraftPresent(false);
    setDraftError(null);
    setDraftSaving(false);
    setDraftRemoving(false);
    setCommandCheckPending(false);
    setPendingCommand(null);
    setPendingDelivery(false);
    setPendingCommandRestored(false);
    setDraftFormatUnknown(false);
  }, [persistNewClientDraft, client, open, offlineStorageStaffId, offlineStorageStatus]);

  useEffect(() => {
    const generation = ++restoreGeneration.current;
    if (!persistNewClientDraft || client !== null || !open) {
      setDraftReady(false);
      return;
    }
    if (offlineStorageStatus !== "unlocked" || !offlineStorageStaffId || !offlineStorageGet) {
      setDraftReady(false);
      return;
    }
    if (!allowDraftRestore.current) {
      setDraftReady(true);
      return;
    }
    const typedAtStart = typedGeneration.current;
    setDraftReady(false);
    setDraftError(null);
    setCommandCheckPending(true);
    const load = async () => {
      const commandValue = await offlineStorageGet("new-client-command");
      if (generation !== restoreGeneration.current) return { kind: "command-invalid" as const };
      if (commandValue !== undefined) {
        setPendingDelivery(true);
        setDraftPresent(true);
        if (!isNewClientCommand(commandValue)) {
          setDraftError(
            "Есть незавершённая отправка неизвестного формата. Удалять её автоматически нельзя.",
          );
          setPendingCommandRestored(false);
          setDraftFormatUnknown(true);
          return { kind: "command-invalid" as const };
        }
        setPendingCommand(commandValue);
        if (typedGeneration.current === typedAtStart) {
          const restored = commandValue.draft;
          originalDraft.current = restored;
          setDraft(restored);
          setPendingCommandRestored(true);
        } else {
          setPendingCommandRestored(false);
        }
        return { kind: "command" as const };
      }
      if (!unconfirmedCreate) {
        setPendingDelivery(false);
        setPendingCommand(null);
        setPendingCommandRestored(false);
      }
      setCommandCheckPending(false);
      try {
        return { kind: "draft" as const, value: await offlineStorageGet("new-client-draft") };
      } catch {
        return { kind: "draft-read-error" as const };
      }
    };
    void load()
      .then((result) => {
        if (generation !== restoreGeneration.current) return;
        setCommandCheckPending(false);
        if (result.kind === "command-invalid") {
          setDraftReady(true);
          return;
        }
        if (result.kind === "draft-read-error") {
          setDraftError("Не удалось прочитать локальный черновик.");
          setDraftPresent(true);
          setDraftFormatUnknown(true);
          setDraftReady(true);
          return;
        }
        if (result.kind === "command") {
          setDraftReady(true);
          return;
        }
        if (typedGeneration.current !== typedAtStart) {
          setDraftReady(true);
          return;
        }
        const value = result.value;
        if (value === undefined) {
          setDraftPresent(false);
          setDraftReady(true);
          return;
        }
        if (!isNewClientDraft(value)) {
          setDraftError(
            "Сохранённый черновик имеет неизвестный формат. Удалите его, чтобы начать заново.",
          );
          setDraftPresent(true);
          setDraftFormatUnknown(true);
          setDraftReady(true);
          return;
        }
        const restored = {
          firstName: value.firstName,
          lastName: value.lastName,
          middleName: value.middleName,
          birthdate: value.birthdate,
          sourceId: value.sourceId,
          contacts: value.contacts,
        };
        originalDraft.current = restored;
        setDraft(restored);
        setDraftPresent(true);
        setDraftFormatUnknown(false);
        setDraftReady(true);
      })
      .catch(() => {
        if (generation !== restoreGeneration.current) return;
        setCommandCheckPending(false);
        setPendingDelivery(true);
        setPendingCommandRestored(false);
        setDraftError(
          "Не удалось проверить незавершённую отправку. Повтор заблокирован до разблокировки хранилища.",
        );
        setDraftReady(true);
      });
    return () => {
      if (generation === restoreGeneration.current) restoreGeneration.current += 1;
    };
  }, [
    persistNewClientDraft,
    client,
    open,
    offlineStorageStaffId,
    offlineStorageStatus,
    unconfirmedCreate,
    offlineStorageGet,
  ]);

  useEffect(() => {
    if (
      !canPersistDraft ||
      !draftReady ||
      typedGeneration.current === 0 ||
      saving ||
      pendingDelivery ||
      unconfirmedCreate ||
      draftFormatUnknown
    )
      return;
    const storage = offlineStorageRef.current;
    if (!storage) return;
    const generation = restoreGeneration.current;
    const staffId = storage.staffId;
    pendingSaveCount.current += 1;
    setDraftSaving(true);
    setDraftError(null);
    saveQueue.current = saveQueue.current
      .catch(() => undefined)
      .then(() => {
        const currentStorage = offlineStorageRef.current;
        if (
          generation !== restoreGeneration.current ||
          currentStorage?.status !== "unlocked" ||
          currentStorage.staffId !== staffId
        )
          return;
        return storage.put("new-client-draft", toStoredNewClientDraft(draft), "pending");
      })
      .then(() => {
        if (generation === restoreGeneration.current) setDraftPresent(true);
      })
      .catch(() => {
        if (generation === restoreGeneration.current)
          setDraftError("Не удалось сохранить локальный черновик.");
      })
      .finally(() => {
        pendingSaveCount.current -= 1;
        if (pendingSaveCount.current === 0) setDraftSaving(false);
      });
  }, [
    canPersistDraft,
    draftReady,
    draft,
    saving,
    pendingDelivery,
    unconfirmedCreate,
    draftFormatUnknown,
  ]);

  useEffect(() => {
    if (draftSavedKey === 0) return;
    ++restoreGeneration.current;
    allowDraftRestore.current = false;
    typedGeneration.current = 0;
    const empty = draftFromClient(null);
    originalDraft.current = empty;
    setDraft(empty);
    setDraftError(null);
    setDraftPresent(false);
    setDraftReady(true);
    setPendingDelivery(false);
    setPendingCommand(null);
    setPendingCommandRestored(false);
    setDraftFormatUnknown(false);
  }, [draftSavedKey]);

  useEffect(() => {
    if (unconfirmedCreate) {
      setPendingDelivery(true);
      setPendingCommandRestored(true);
    } else if (unconfirmedWasSet.current) {
      setPendingDelivery(false);
      setPendingCommand(null);
      setPendingCommandRestored(false);
    }
    unconfirmedWasSet.current = unconfirmedCreate;
  }, [unconfirmedCreate]);

  useErrorNotification(stale ? null : error, { title: "Ошибка при работе с карточкой клиента" });

  useEffect(() => {
    if (validation) form.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
  }, [validation]);

  function fieldValidation(key: string) {
    const invalid = validation?.fields.includes(key) || undefined;
    return {
      "aria-invalid": invalid,
      "aria-describedby": invalid ? `${formId}-error` : undefined,
    };
  }

  useLayoutEffect(() => {
    const session = { open, clientId: clientId ?? null, resetKey };
    const newSession =
      open &&
      (!previousSession.current.open ||
        previousSession.current.clientId !== session.clientId ||
        previousSession.current.resetKey !== resetKey);
    previousSession.current = session;
    if (!newSession) return;
    allowDraftRestore.current = true;
    typedGeneration.current = 0;
    const next = draftFromClient(latestClient.current);
    originalDraft.current = next;
    setDraft(next);
    setValidation(null);
  }, [open, clientId, resetKey]);

  function update<K extends keyof ClientCardDraft>(key: K, value: ClientCardDraft[K]) {
    typedGeneration.current += 1;
    setDraft((previous) => ({ ...previous, [key]: value }));
    setValidation(null);
  }

  function updateContact(index: number, patch: Partial<ClientContactDraft>) {
    typedGeneration.current += 1;
    setDraft((previous) => {
      const contacts = previous.contacts.map((contact, contactIndex) =>
        contactIndex === index ? { ...contact, ...patch } : contact,
      );
      if (patch.isPrimary === true) {
        const kindId = contacts[index]?.kindId;
        if (kindId) {
          for (let contactIndex = 0; contactIndex < contacts.length; contactIndex++) {
            const contact = contacts[contactIndex];
            if (contactIndex !== index && contact?.kindId === kindId)
              contacts[contactIndex] = { ...contact, isPrimary: false };
          }
        }
      }
      const contact = contacts[index];
      if (
        contact &&
        patch.kindId &&
        !contacts.some(
          (contact, contactIndex) =>
            contactIndex !== index && contact.kindId === patch.kindId && contact.isPrimary,
        )
      )
        contacts[index] = { ...contact, isPrimary: true };
      return { ...previous, contacts };
    });
    setValidation(null);
  }

  function addContact() {
    typedGeneration.current += 1;
    const kindId = contactKinds.find((kind) => !kind.isArchived)?.id ?? "";
    setDraft((previous) => ({
      ...previous,
      contacts: [
        ...previous.contacts,
        {
          id: uuidv7(),
          kindId,
          value: "",
          comment: "",
          isPrimary: kindId
            ? !previous.contacts.some((contact) => contact.kindId === kindId && contact.isPrimary)
            : false,
        },
      ],
    }));
    setValidation(null);
  }

  async function submit() {
    if (
      loading ||
      saving ||
      stale ||
      !vaultAccessAllowed ||
      commandCheckPending ||
      (persistNewClientDraft && offlineStorage?.status === "unlocked" && !draftReady) ||
      (pendingDelivery && !pendingCommandRestored)
    )
      return;
    const invalidInput = Array.from(form.current?.elements ?? []).find(
      (element): element is HTMLInputElement =>
        element instanceof HTMLInputElement && element.validity.customError,
    );
    if (invalidInput) {
      invalidInput.reportValidity();
      invalidInput.focus();
      return;
    }
    const issue = validationError(draft);
    if (issue) {
      setValidation(issue);
      return;
    }
    setValidation(null);
    if (
      persistNewClientDraft &&
      client === null &&
      offlineStorageRef.current?.status === "unlocked"
    ) {
      const submitGeneration = restoreGeneration.current;
      const pendingAtSubmit = pendingDelivery;
      await saveQueue.current;
      if (
        submitGeneration !== restoreGeneration.current ||
        !isDraftSubmitAllowed() ||
        pendingDeliveryRef.current !== pendingAtSubmit ||
        (unconfirmedCreateRef.current && !pendingCommandRestoredRef.current)
      )
        return;
    }
    onSave({
      ...draft,
      firstName:
        draft.firstName === originalDraft.current.firstName
          ? draft.firstName
          : draft.firstName.trim(),
      lastName:
        draft.lastName === originalDraft.current.lastName ? draft.lastName : draft.lastName.trim(),
      middleName:
        draft.middleName === originalDraft.current.middleName
          ? draft.middleName
          : draft.middleName?.trim() || null,
      birthdate: draft.birthdate || null,
      contacts: draft.contacts.map((contact) => ({
        ...contact,
        value: contact.value,
        comment:
          contact.comment ===
          originalDraft.current.contacts.find((original) => original.id === contact.id)?.comment
            ? contact.comment
            : contact.comment?.trim() || null,
      })),
    });
  }

  async function removeDraft() {
    if (
      pendingDelivery ||
      unconfirmedCreate ||
      offlineStorage?.status !== "unlocked" ||
      !offlineStorage.staffId
    )
      return;
    ++restoreGeneration.current;
    const generation = restoreGeneration.current;
    const staffId = offlineStorage.staffId;
    allowDraftRestore.current = false;
    typedGeneration.current = 0;
    setDraftRemoving(true);
    try {
      const task = saveQueue.current.then(() => {
        const currentStorage = offlineStorageRef.current;
        if (
          generation !== restoreGeneration.current ||
          currentStorage?.status !== "unlocked" ||
          currentStorage.staffId !== staffId
        )
          return;
        return offlineStorage.remove("new-client-draft");
      });
      removeTask.current = task;
      await task;
      const empty = draftFromClient(null);
      originalDraft.current = empty;
      setDraft(empty);
      setDraftPresent(false);
      setDraftReady(true);
      setDraftFormatUnknown(false);
      setDraftError(null);
    } catch {
      setDraftError("Не удалось удалить локальный черновик.");
    } finally {
      setDraftRemoving(false);
    }
  }

  async function closeEditor() {
    if (!vaultAccessAllowed) {
      ++restoreGeneration.current;
      for (const cancel of closeWaitCancels.current) cancel();
      closeWaitCancels.current.clear();
      saveQueue.current = Promise.resolve();
      removeTask.current = Promise.resolve();
      onClose();
      return;
    }
    let cancelWait: () => void = () => undefined;
    const cancelled = new Promise<void>((resolve) => {
      cancelWait = resolve;
      closeWaitCancels.current.add(resolve);
    });
    try {
      await Promise.race([Promise.all([saveQueue.current, removeTask.current]), cancelled]);
    } finally {
      closeWaitCancels.current.delete(cancelWait);
      onClose();
    }
  }

  function restorePendingCommand() {
    if (!pendingCommand) return;
    originalDraft.current = pendingCommand.draft;
    setDraft(pendingCommand.draft);
    typedGeneration.current = 0;
    setPendingCommandRestored(true);
    setDraftError(null);
  }

  const saveDisabled =
    loading ||
    stale ||
    !vaultAccessAllowed ||
    commandCheckPending ||
    (persistNewClientDraft && offlineStorage?.status === "unlocked" && !draftReady) ||
    (pendingDelivery && !pendingCommandRestored);

  return (
    <PaperDrawer
      open={open}
      title={client ? "Изменить карточку" : "Новый клиент"}
      onClose={() => void closeEditor()}
      footer={
        <div className={styles["client-editor-footer"]}>
          <PaperButton onClick={() => void closeEditor()} disabled={saving}>
            Отмена
          </PaperButton>
          <PaperButton
            variant="primary"
            type="submit"
            form={formId}
            loading={saving}
            disabled={saveDisabled}
          >
            {pendingDelivery ? "Повторить отправку" : "Сохранить"}
          </PaperButton>
        </div>
      }
    >
      {loading ? <p role="status">Загрузка карточки…</p> : null}
      {stale ? (
        <div
          className={`${styles["client-editor-message"]} ${styles["client-editor-message-warning"]}`}
          role="status"
        >
          <p>Карточка изменилась на сервере. Ваш ввод сохранён.</p>
          {onReload ? (
            <PaperButton size="small" onClick={onReload}>
              Загрузить актуальные данные
            </PaperButton>
          ) : null}
        </div>
      ) : null}
      {hasDuplicateContacts ? (
        <p
          className={`${styles["client-editor-message"]} ${styles["client-editor-message-warning"]}`}
          role="status"
        >
          Один из контактов уже указан у другого клиента. Карточки не объединены.
        </p>
      ) : null}
      {persistNewClientDraft && client === null && open ? (
        <section className={styles["client-editor-draft"]} aria-label="Локальный черновик">
          {offlineStorage?.status === "unlocked" ? (
            <p role="status">
              {draftSaving
                ? "Сохраняем черновик на устройстве…"
                : "Черновик хранится на этом устройстве и не отправляется автоматически."}
            </p>
          ) : offlineStorage?.status === "error" ? (
            <p role="alert">
              Локальное хранилище недоступно. Черновик на этом устройстве не сохранён.
            </p>
          ) : offlineStorage?.status === "blocked" ? (
            <p role="alert">
              Локальные данные закрыты после выхода. Черновик на этом устройстве недоступен.
            </p>
          ) : (
            <p role="status">Подготавливаем локальное хранилище…</p>
          )}
          {draftError ? (
            <p
              className={`${styles["client-editor-message"]} ${styles["client-editor-message-error"]}`}
              role="alert"
            >
              {draftError}
            </p>
          ) : null}
          {pendingDelivery ? (
            <div
              className={`${styles["client-editor-message"]} ${styles["client-editor-message-warning"]}`}
              role="status"
            >
              <p>Предыдущая отправка не подтверждена. Повтор использует ту же команду.</p>
              {pendingCommand && !pendingCommandRestored ? (
                <PaperButton size="small" onClick={restorePendingCommand}>
                  Восстановить отправку
                </PaperButton>
              ) : null}
            </div>
          ) : null}
          {draftPresent ? (
            <PaperButton
              variant="danger"
              onClick={() => void removeDraft()}
              disabled={
                draftSaving ||
                draftRemoving ||
                offlineStorage?.status !== "unlocked" ||
                pendingDelivery ||
                unconfirmedCreate
              }
            >
              Удалить черновик
            </PaperButton>
          ) : null}
        </section>
      ) : null}
      {validation ? (
        <div
          id={`${formId}-error`}
          className={`${styles["client-editor-message"]} ${styles["client-editor-message-warning"]}`}
          role="alert"
        >
          {validation.message}
        </div>
      ) : null}
      <form
        id={formId}
        ref={form}
        noValidate
        aria-label={client ? "Изменить карточку" : "Новый клиент"}
        onSubmit={(event) => {
          event.preventDefault();
          void submit();
        }}
      >
        <fieldset
          className={styles["client-editor-form"]}
          disabled={
            loading ||
            saving ||
            commandCheckPending ||
            draftRemoving ||
            pendingDelivery ||
            unconfirmedCreate ||
            draftFormatUnknown ||
            !vaultAccessAllowed
          }
        >
          <div className={styles["client-editor-field"]}>
            <span>
              Фамилия
              <span className={styles["field-required"]} aria-hidden="true">
                *
              </span>
            </span>
            <PaperInput
              aria-label="Фамилия"
              aria-required="true"
              {...fieldValidation("lastName")}
              autoComplete="family-name"
              value={draft.lastName}
              onChange={(event) => update("lastName", event.target.value)}
            />
          </div>
          <div className={styles["client-editor-field"]}>
            <span>
              Имя
              <span className={styles["field-required"]} aria-hidden="true">
                *
              </span>
            </span>
            <PaperInput
              aria-label="Имя"
              aria-required="true"
              {...fieldValidation("firstName")}
              autoComplete="given-name"
              value={draft.firstName}
              onChange={(event) => update("firstName", event.target.value)}
            />
          </div>
          <div className={styles["client-editor-field"]}>
            <span>Отчество</span>
            <PaperInput
              aria-label="Отчество"
              value={draft.middleName ?? ""}
              onChange={(event) => update("middleName", event.target.value)}
            />
          </div>
          <div className={styles["client-editor-field"]}>
            <span>Дата рождения</span>
            <DateInput
              aria-label="Дата рождения"
              value={draft.birthdate}
              disabled={loading || saving}
              onChange={(value) => update("birthdate", value)}
            />
          </div>
          <div className={styles["client-editor-field"]}>
            <span>
              Источник
              <span className={styles["field-required"]} aria-hidden="true">
                *
              </span>
            </span>
            <PaperSelect
              aria-label="Источник"
              aria-required="true"
              {...fieldValidation("sourceId")}
              value={draft.sourceId || null}
              placeholder="Выберите источник"
              options={sourceOptions.map((source) => ({ value: source.id, label: source.name }))}
              onValueChange={(sourceId) => update("sourceId", sourceId)}
            />
          </div>
          <section className={styles["client-editor-contacts"]} aria-label="Контакты">
            <div className={styles["client-editor-section-heading"]}>
              <h3>Контакты</h3>
              <PaperButton
                icon={<Icon name="plus" />}
                onClick={addContact}
                disabled={!contactKinds.some((kind) => !kind.isArchived)}
              >
                Добавить контакт
              </PaperButton>
            </div>
            {!draft.contacts.length ? (
              <p className={styles["directory-muted"]}>Контакты можно добавить позже.</p>
            ) : null}
            {draft.contacts.map((contact, index) => {
              const currentKind = contactKinds.find((kind) => kind.id === contact.kindId);
              const kindOptions = contactKinds.filter(
                (kind) => !kind.isArchived || kind.id === contact.kindId,
              );
              return (
                <div className={styles["client-editor-contact"]} key={contact.id}>
                  <div className={styles["client-editor-field"]}>
                    <span>
                      Вид контакта
                      <span className={styles["field-required"]} aria-hidden="true">
                        *
                      </span>
                    </span>
                    <PaperSelect
                      aria-label={`Вид контакта ${index + 1}`}
                      aria-required="true"
                      {...fieldValidation(`kind-${contact.id}`)}
                      value={contact.kindId || null}
                      placeholder="Выберите вид"
                      options={kindOptions.map((kind) => ({ value: kind.id, label: kind.name }))}
                      onValueChange={(kindId) => updateContact(index, { kindId })}
                    />
                  </div>
                  <div className={styles["client-editor-field"]}>
                    <span>
                      Значение
                      <span className={styles["field-required"]} aria-hidden="true">
                        *
                      </span>
                    </span>
                    <PaperInput
                      aria-label={`Значение контакта ${index + 1}`}
                      aria-required="true"
                      {...fieldValidation(`value-${contact.id}`)}
                      type={currentKind?.format === "email" ? "email" : "text"}
                      autoComplete={
                        currentKind?.format === "phone"
                          ? "tel"
                          : currentKind?.format === "email"
                            ? "email"
                            : "url"
                      }
                      value={contact.value}
                      onChange={(event) => updateContact(index, { value: event.target.value })}
                    />
                  </div>
                  <div className={styles["client-editor-field"]}>
                    <span>Комментарий</span>
                    <PaperInput
                      aria-label={`Комментарий контакта ${index + 1}`}
                      value={contact.comment ?? ""}
                      onChange={(event) => updateContact(index, { comment: event.target.value })}
                    />
                  </div>
                  <div className={styles["client-editor-contact-actions"]}>
                    <label className={styles["client-editor-primary"]}>
                      <input
                        type="radio"
                        {...fieldValidation(`primary-${contact.id}`)}
                        name={`primary-${contact.kindId || index}`}
                        checked={contact.isPrimary}
                        disabled={!contact.kindId}
                        onChange={() => updateContact(index, { isPrimary: true })}
                      />{" "}
                      Основной
                    </label>
                    <PaperTooltip title="Удалить контакт">
                      <PaperButton
                        variant="danger"
                        aria-label="Удалить"
                        icon={<Icon name="trash" />}
                        onClick={() =>
                          update(
                            "contacts",
                            draft.contacts.filter((_, contactIndex) => contactIndex !== index),
                          )
                        }
                      />
                    </PaperTooltip>
                  </div>
                </div>
              );
            })}
          </section>
        </fieldset>
      </form>
    </PaperDrawer>
  );
}
