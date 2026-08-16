import type { FormInstance } from "antd";
import { Form, Input } from "antd";

import { formatPhoneInput, isValidPhone, normalizePhone, normalizeSocialLink } from "@/entities/client";
import type { User } from "@/entities/user";
import { formatRecordActivitySummary } from "@/shared/lib";
import type { DurableFormStatus } from "@/shared/lib/react";
import { DraftFormModal, StatusBanner } from "@/shared/ui";

export type UserFormValues = {
  firstName: string;
  lastName: string;
  phone?: string | null;
  telegram?: string | null;
  vk?: string | null;
};

export function UserEditorModal({
  open,
  form,
  savePending,
  isStale,
  staleActivity,
  onCancel,
  onSubmit,
  draftStatus,
  draftRestored,
  hasDraft,
  onDiscardDraft,
  onValuesChange,
  draftStale,
  onReapplyDraft,
  onRetryDraft,
}: {
  open: boolean;
  form: FormInstance<UserFormValues>;
  savePending: boolean;
  isStale: boolean;
  staleActivity?: User["lastActivity"];
  onCancel: () => void;
  onSubmit: (values: UserFormValues) => void;
  draftStatus: DurableFormStatus;
  draftRestored: boolean;
  hasDraft: boolean;
  onDiscardDraft: () => void;
  onValuesChange?: (_: Partial<UserFormValues>, values: UserFormValues) => void;
  draftStale: boolean;
  onReapplyDraft: () => void;
  onRetryDraft: () => void;
}) {
  return (
    <DraftFormModal
      open={open}
      title="Редактировать пользователя"
      restored={draftRestored}
      saveStatus={draftStatus}
      showClearDraft={hasDraft}
      onClearDraft={onDiscardDraft}
      stale={draftStale}
      onReapplyDraft={onReapplyDraft}
      onRetryDraft={onRetryDraft}
      onCancel={onCancel}
      onOk={() => {
        form.submit();
      }}
      confirmLoading={savePending}
    >
      <Form form={form} layout="vertical" requiredMark={false} onFinish={onSubmit} onValuesChange={onValuesChange}>
        {isStale ? (
          <StatusBanner
            type="warning"
            title="Карточка пользователя изменилась в другом окне"
            description={formatRecordActivitySummary(staleActivity)}
          />
        ) : null}
        <Form.Item name="lastName" label="Фамилия" rules={[{ required: true }]}>
          <Input />
        </Form.Item>
        <Form.Item name="firstName" label="Имя" rules={[{ required: true }]}>
          <Input />
        </Form.Item>
        <Form.Item
          name="phone"
          label="Телефон"
          rules={[
            {
              validator: (_, value?: string) =>
                !value?.trim() || isValidPhone(value) ? Promise.resolve() : Promise.reject(new Error("Введите корректный номер телефона")),
            },
          ]}
        >
          <PhoneInput />
        </Form.Item>
        <Form.Item
          name="telegram"
          label="Telegram"
          rules={[
            {
              validator: (_, value?: string) =>
                !value?.trim() || normalizeSocialLink(value, "telegram")
                  ? Promise.resolve()
                  : Promise.reject(new Error("Введите Telegram как @nickname, nickname или ссылку t.me")),
            },
          ]}
        >
          <Input placeholder="@nickname или https://t.me/nickname" />
        </Form.Item>
        <Form.Item
          name="vk"
          label="VK"
          rules={[
            {
              validator: (_, value?: string) =>
                !value?.trim() || normalizeSocialLink(value, "vk")
                  ? Promise.resolve()
                  : Promise.reject(new Error("Введите VK как nickname или ссылку vk.com/vk.ru")),
            },
          ]}
        >
          <Input placeholder="nickname или https://vk.com/nickname" />
        </Form.Item>
      </Form>
    </DraftFormModal>
  );
}

function PhoneInput({ value, onChange }: { value?: string | null; onChange?: (value?: string) => void }) {
  return (
    <Input
      value={value ?? ""}
      inputMode="tel"
      autoComplete="tel"
      placeholder="+49 1512 3456789"
      onChange={(event) => {
        onChange?.(formatPhoneInput(event.target.value) || undefined);
      }}
      onBlur={(event) => {
        const normalized = normalizePhone(event.target.value);
        onChange?.(normalized ? formatPhoneInput(normalized) || undefined : formatPhoneInput(event.target.value) || undefined);
      }}
    />
  );
}
