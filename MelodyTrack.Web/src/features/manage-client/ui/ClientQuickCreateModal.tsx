import { useMutation } from "@tanstack/react-query";
import { App as AntdApp, Form } from "antd";
import { useState } from "react";
import * as v from "valibot";

import { clientsApi, normalizePhone, normalizeSocialLink } from "@/entities/client";
import { clientSourcesApi } from "@/entities/reference-book";
import { useCreatedReferenceOptions } from "@/shared/lib";
import { jsonDurableFormCodec, useDurableForm } from "@/shared/lib/react";
import { DraftFormModal } from "@/shared/ui";
import { ReferenceBookCreateModal } from "@/shared/ui/ReferenceBookCreateModal";

import { ClientFormFields } from "./ClientFormFields";

type ClientQuickCreateValues = {
  firstName?: string;
  lastName?: string;
  patronymic?: string;
  phone?: string;
  telegram?: string;
  vk?: string;
  sourceId?: string;
};

type ClientQuickCreateModalProps = {
  open: boolean;
  onCancel: () => void;
  onCreated: (client: { id: string; displayName: string }) => void;
};

const clientQuickCreateSchema = v.object({
  firstName: v.optional(v.string()),
  lastName: v.optional(v.string()),
  patronymic: v.optional(v.string()),
  phone: v.optional(v.string()),
  telegram: v.optional(v.string()),
  vk: v.optional(v.string()),
  sourceId: v.optional(v.string()),
});
const clientQuickCreateCodec = jsonDurableFormCodec<ClientQuickCreateValues>();

export function ClientQuickCreateModal({ open, onCancel, onCreated }: ClientQuickCreateModalProps) {
  const [form] = Form.useForm<ClientQuickCreateValues>();
  const [isSourceCreateOpen, setSourceCreateOpen] = useState(false);
  const createdSourceOptions = useCreatedReferenceOptions("client-source");
  const draft = useDurableForm({
    key: "draft:clients:quick-create",
    schema: clientQuickCreateSchema,
    form,
    codec: clientQuickCreateCodec,
    enabled: open,
  });
  const { message } = AntdApp.useApp();
  const createMutation = useMutation({
    mutationFn: (values: ClientQuickCreateValues) =>
      clientsApi.create({
        firstName: values.firstName ?? "",
        lastName: values.lastName ?? "",
        patronymic: values.patronymic?.trim() || undefined,
        phone: normalizePhone(values.phone),
        telegram: normalizeSocialLink(values.telegram, "telegram"),
        vk: normalizeSocialLink(values.vk, "vk"),
        sourceId: values.sourceId,
      }),
    onSuccess: async (result, values) => {
      message.success("Клиент создан");
      await draft.clearAfterSuccess();
      form.resetFields();
      onCreated({
        id: result.id,
        displayName: formatClientName(values),
      });
    },
  });

  const createSourceMutation = useMutation({
    mutationFn: (values: { name: string }) => clientSourcesApi.create(values),
    onSuccess: (result, values) => {
      message.success("Источник создан");
      createdSourceOptions.addCreatedOption({ id: result.id, label: values.name.trim() });
      form.setFieldValue("sourceId", result.id);
      setSourceCreateOpen(false);
    },
  });

  return (
    <DraftFormModal
      open={open}
      title="Новый клиент"
      restored={draft.restored}
      saveStatus={draft.status}
      showClearDraft={draft.hasDraft}
      onClearDraft={() => {
        void draft.discard().then(() => {
          form.resetFields();
        });
      }}
      onRetryDraft={draft.retry}
      onCancel={() => {
        onCancel();
      }}
      onOk={() => {
        form.submit();
      }}
      confirmLoading={createMutation.isPending}
      destroyOnHidden
    >
      <Form
        form={form}
        layout="vertical"
        requiredMark={false}
        onValuesChange={draft.formProps.onValuesChange}
        onFinish={(values) => {
          createMutation.mutate(values);
        }}
      >
        <ClientFormFields
          sourceOptions={createdSourceOptions.createdOptions}
          onCreateSource={() => {
            setSourceCreateOpen(true);
          }}
        />
      </Form>
      <ReferenceBookCreateModal
        open={isSourceCreateOpen}
        title="Новый источник клиента"
        draftKey="draft:client-sources:create"
        confirmLoading={createSourceMutation.isPending}
        onCancel={() => {
          setSourceCreateOpen(false);
        }}
        onSubmit={(values, clearAfterSuccess) => {
          createSourceMutation.mutate(values, { onSuccess: () => void clearAfterSuccess() });
        }}
      />
    </DraftFormModal>
  );
}

function formatClientName(values: ClientQuickCreateValues) {
  return [values.lastName, values.firstName, values.patronymic].filter(Boolean).join(" ");
}
