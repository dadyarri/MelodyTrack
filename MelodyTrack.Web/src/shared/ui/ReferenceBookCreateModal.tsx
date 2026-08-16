import { Form, Input } from "antd";
import * as v from "valibot";

import { jsonDurableFormCodec, useDurableForm } from "@/shared/lib/react";

import { DraftFormModal } from "./DraftFormModal";

type ReferenceBookCreateModalProps = {
  open: boolean;
  title: string;
  draftKey: string;
  confirmLoading?: boolean;
  onCancel: () => void;
  onSubmit: (values: { name: string }, clearAfterSuccess: () => Promise<void>) => void;
};

const referenceBookDraftSchema = v.object({ name: v.optional(v.string()) });
type ReferenceBookDraftValues = { name?: string };
const referenceBookDraftCodec = jsonDurableFormCodec<ReferenceBookDraftValues>();

export function ReferenceBookCreateModal({
  open,
  title,
  draftKey,
  confirmLoading = false,
  onCancel,
  onSubmit,
}: ReferenceBookCreateModalProps) {
  const [form] = Form.useForm<ReferenceBookDraftValues>();
  const draft = useDurableForm({ key: draftKey, schema: referenceBookDraftSchema, form, codec: referenceBookDraftCodec, enabled: open });
  const clearAfterSuccess = async () => {
    await draft.clearAfterSuccess();
    form.resetFields();
  };

  return (
    <DraftFormModal
      open={open}
      title={title}
      restored={draft.restored}
      saveStatus={draft.status}
      showClearDraft={draft.hasDraft}
      onClearDraft={() => {
        void draft.discard().then(() => {
          form.resetFields();
        });
      }}
      onRetryDraft={draft.retry}
      onCancel={onCancel}
      onOk={() => {
        form.submit();
      }}
      confirmLoading={confirmLoading}
      destroyOnHidden
    >
      <Form
        form={form}
        layout="vertical"
        requiredMark={false}
        onValuesChange={draft.formProps.onValuesChange}
        onFinish={(values) => {
          if (values.name) onSubmit({ name: values.name }, clearAfterSuccess);
        }}
      >
        <Form.Item name="name" label="Название" rules={[{ required: true }]}>
          <Input />
        </Form.Item>
      </Form>
    </DraftFormModal>
  );
}
