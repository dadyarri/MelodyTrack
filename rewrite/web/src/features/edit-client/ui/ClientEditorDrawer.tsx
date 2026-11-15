import { Alert, Button, Drawer, Input, Select, Space, Tooltip } from "antd";
import { useEffect, useId, useRef, useState } from "react";
import type { DirectoryClient } from "@/entities/client-directory";
import { uuidv7 } from "@/shared/lib/uuidv7";
import { DateInput } from "@/shared/ui/date-time";
import { Icon } from "@/shared/ui/Icon";
import { useErrorNotification } from "@/shared/ui/notifications";

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
  latestClient.current = client;

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

  useEffect(() => {
    const session = { open, clientId: clientId ?? null, resetKey };
    const newSession =
      open &&
      (!previousSession.current.open ||
        previousSession.current.clientId !== session.clientId ||
        previousSession.current.resetKey !== resetKey);
    previousSession.current = session;
    if (!newSession) return;
    const next = draftFromClient(latestClient.current);
    originalDraft.current = next;
    setDraft(next);
    setValidation(null);
  }, [open, clientId, resetKey]);

  function update<K extends keyof ClientCardDraft>(key: K, value: ClientCardDraft[K]) {
    setDraft((previous) => ({ ...previous, [key]: value }));
    setValidation(null);
  }

  function updateContact(index: number, patch: Partial<ClientContactDraft>) {
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

  function submit() {
    if (loading || saving || stale) return;
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

  return (
    <Drawer
      open={open}
      placement="right"
      size="min(560px, 100vw)"
      title={client ? "Изменить карточку" : "Новый клиент"}
      onClose={onClose}
      destroyOnHidden
      classNames={{ root: "directory-drawer" }}
      extra={
        <Space>
          <Button onClick={onClose} disabled={saving}>
            Отмена
          </Button>
          <Button
            type="primary"
            htmlType="submit"
            form={formId}
            loading={saving}
            disabled={loading || stale}
          >
            Сохранить
          </Button>
        </Space>
      }
    >
      {loading ? <p role="status">Загрузка карточки…</p> : null}
      {stale ? (
        <Alert
          type="warning"
          showIcon
          title="Карточка изменилась на сервере. Ваш ввод сохранён."
          action={
            onReload ? (
              <Button size="small" onClick={onReload}>
                Загрузить актуальные данные
              </Button>
            ) : undefined
          }
        />
      ) : null}
      {hasDuplicateContacts ? (
        <Alert
          type="warning"
          showIcon
          title="Один из контактов уже указан у другого клиента. Карточки не объединены."
        />
      ) : null}
      {validation ? (
        <div id={`${formId}-error`}>
          <Alert type="warning" showIcon title={validation.message} />
        </div>
      ) : null}
      <form
        id={formId}
        ref={form}
        noValidate
        aria-label={client ? "Изменить карточку" : "Новый клиент"}
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }}
      >
        <fieldset className="client-editor-form" disabled={loading}>
          <div className="client-editor-field">
            <span>
              Фамилия
              <span className="field-required" aria-hidden="true">
                *
              </span>
            </span>
            <Input
              aria-label="Фамилия"
              aria-required="true"
              {...fieldValidation("lastName")}
              status={validation?.fields.includes("lastName") ? "error" : ""}
              autoComplete="family-name"
              value={draft.lastName}
              onChange={(event) => update("lastName", event.target.value)}
            />
          </div>
          <div className="client-editor-field">
            <span>
              Имя
              <span className="field-required" aria-hidden="true">
                *
              </span>
            </span>
            <Input
              aria-label="Имя"
              aria-required="true"
              {...fieldValidation("firstName")}
              status={validation?.fields.includes("firstName") ? "error" : ""}
              autoComplete="given-name"
              value={draft.firstName}
              onChange={(event) => update("firstName", event.target.value)}
            />
          </div>
          <div className="client-editor-field">
            <span>Отчество</span>
            <Input
              aria-label="Отчество"
              value={draft.middleName ?? ""}
              onChange={(event) => update("middleName", event.target.value)}
            />
          </div>
          <div className="client-editor-field">
            <span>Дата рождения</span>
            <DateInput
              aria-label="Дата рождения"
              value={draft.birthdate}
              disabled={loading || saving}
              onChange={(value) => update("birthdate", value)}
            />
          </div>
          <div className="client-editor-field">
            <span>
              Источник
              <span className="field-required" aria-hidden="true">
                *
              </span>
            </span>
            <Select
              aria-label="Источник"
              aria-required="true"
              {...fieldValidation("sourceId")}
              status={validation?.fields.includes("sourceId") ? "error" : ""}
              value={draft.sourceId || null}
              placeholder="Выберите источник"
              options={sourceOptions.map((source) => ({ value: source.id, label: source.name }))}
              onChange={(sourceId: string) => update("sourceId", sourceId)}
            />
          </div>
          <section className="client-editor-contacts" aria-label="Контакты">
            <div className="client-editor-section-heading">
              <h3>Контакты</h3>
              <Button
                icon={<Icon name="plus" />}
                onClick={addContact}
                disabled={!contactKinds.some((kind) => !kind.isArchived)}
              >
                Добавить контакт
              </Button>
            </div>
            {!draft.contacts.length ? (
              <p className="directory-muted">Контакты можно добавить позже.</p>
            ) : null}
            {draft.contacts.map((contact, index) => {
              const currentKind = contactKinds.find((kind) => kind.id === contact.kindId);
              const kindOptions = contactKinds.filter(
                (kind) => !kind.isArchived || kind.id === contact.kindId,
              );
              return (
                <div className="client-editor-contact" key={contact.id}>
                  <div className="client-editor-field">
                    <span>
                      Вид контакта
                      <span className="field-required" aria-hidden="true">
                        *
                      </span>
                    </span>
                    <Select
                      aria-label={`Вид контакта ${index + 1}`}
                      aria-required="true"
                      {...fieldValidation(`kind-${contact.id}`)}
                      status={validation?.fields.includes(`kind-${contact.id}`) ? "error" : ""}
                      value={contact.kindId || null}
                      placeholder="Выберите вид"
                      options={kindOptions.map((kind) => ({ value: kind.id, label: kind.name }))}
                      onChange={(kindId: string) => updateContact(index, { kindId })}
                    />
                  </div>
                  <div className="client-editor-field">
                    <span>
                      Значение
                      <span className="field-required" aria-hidden="true">
                        *
                      </span>
                    </span>
                    <Input
                      aria-label={`Значение контакта ${index + 1}`}
                      aria-required="true"
                      {...fieldValidation(`value-${contact.id}`)}
                      status={validation?.fields.includes(`value-${contact.id}`) ? "error" : ""}
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
                  <div className="client-editor-field">
                    <span>Комментарий</span>
                    <Input
                      aria-label={`Комментарий контакта ${index + 1}`}
                      value={contact.comment ?? ""}
                      onChange={(event) => updateContact(index, { comment: event.target.value })}
                    />
                  </div>
                  <div className="client-editor-contact-actions">
                    <label className="client-editor-primary">
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
                    <Tooltip title="Удалить контакт">
                      <Button
                        danger
                        aria-label="Удалить"
                        icon={<Icon name="trash" />}
                        onClick={() =>
                          update(
                            "contacts",
                            draft.contacts.filter((_, contactIndex) => contactIndex !== index),
                          )
                        }
                      />
                    </Tooltip>
                  </div>
                </div>
              );
            })}
          </section>
        </fieldset>
      </form>
    </Drawer>
  );
}
