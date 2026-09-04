import type { FormInstance } from "antd";
import { Form } from "antd";
import type { DefaultOptionType } from "antd/es/select";

import type { Client } from "@/entities/client";
import { formatRecordActivitySummary } from "@/shared/lib";
import type { DurableFormStatus } from "@/shared/lib/react";
import { DraftFormModal, StatusBanner } from "@/shared/ui";

import { ClientFormFields } from "./ClientFormFields";

export type ClientFormValues = Omit<Client, "vacations"> & {
  dateOfBirth?: string | null;
  telegram?: string | null;
  vk?: string | null;
  phone?: string | null;
};

export function ClientEditorModal({
  open,
  editing,
  hasDraft,
  draftRestored,
  draftSaveStatus,
  form,
  savePending,
  isStale,
  staleActivity,
  sourceOptions,
  onCancel,
  onClearDraft,
  onSubmit,
  onValuesChange,
  onCreateSource,
  onSourceLabelChange,
  draftStale,
  onReapplyDraft,
  onRetryDraft,
}: {
  open: boolean;
  editing: boolean;
  hasDraft: boolean;
  draftRestored: boolean;
  draftSaveStatus: DurableFormStatus;
  form: FormInstance<ClientFormValues>;
  savePending: boolean;
  isStale: boolean;
  staleActivity?: Client["lastActivity"];
  sourceOptions: DefaultOptionType[];
  onCancel: () => void;
  onClearDraft: () => void;
  onSubmit: (values: ClientFormValues) => void;
  onValuesChange: (_: Partial<ClientFormValues>, values: ClientFormValues) => void;
  onCreateSource?: () => void;
  onSourceLabelChange: (label?: string) => void;
  draftStale: boolean;
  onReapplyDraft: () => void;
  onRetryDraft: () => void;
}) {
  return (
    <DraftFormModal
      open={open}
      title={editing ? "Редактировать клиента" : "Новый клиент"}
      restored={draftRestored}
      saveStatus={draftSaveStatus}
      showClearDraft={hasDraft}
      onClearDraft={onClearDraft}
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
        {editing && isStale ? (
          <StatusBanner
            type="warning"
            title="Карточка клиента изменилась в другом окне"
            description={formatRecordActivitySummary(staleActivity)}
          />
        ) : null}
        <ClientFormFields sourceOptions={sourceOptions} onCreateSource={onCreateSource} onSourceLabelChange={onSourceLabelChange} />
      </Form>
    </DraftFormModal>
  );
}
