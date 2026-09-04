import { Button, DatePicker, Form, Space, Typography } from "antd";
import type { Dayjs } from "dayjs";

import type { Client } from "@/entities/client";
import { DATE_FORMAT } from "@/shared/lib";
import type { DurableFormStatus } from "@/shared/lib/react";
import { DraftFormModal } from "@/shared/ui";

export type ClientVacationsFormValues = {
  vacations?: Array<{ period?: [Dayjs, Dayjs] }>;
};

export function ClientVacationsModal({
  client,
  form,
  saving,
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
  client: Client | null;
  form: ReturnType<typeof Form.useForm<ClientVacationsFormValues>>[0];
  saving: boolean;
  onCancel: () => void;
  onSubmit: (values: ClientVacationsFormValues) => void;
  draftStatus: DurableFormStatus;
  draftRestored: boolean;
  hasDraft: boolean;
  onDiscardDraft: () => void;
  onValuesChange?: (_: Partial<ClientVacationsFormValues>, values: ClientVacationsFormValues) => void;
  draftStale: boolean;
  onReapplyDraft: () => void;
  onRetryDraft: () => void;
}) {
  return (
    <DraftFormModal
      open={client !== null}
      title="Отпуск клиента"
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
      confirmLoading={saving}
    >
      <Typography.Paragraph type="secondary">Встречи, пересекающие эти периоды, не будут показаны или созданы.</Typography.Paragraph>
      <Form form={form} layout="vertical" onFinish={onSubmit} onValuesChange={onValuesChange}>
        <Form.List name="vacations">
          {(fields, { add, remove }) => (
            <Space orientation="vertical" className="wide">
              {fields.map((field) => (
                <Space key={field.key} className="wide">
                  <Form.Item name={[field.name, "period"]} rules={[{ required: true, message: "Укажите период отсутствия" }]} noStyle>
                    <DatePicker.RangePicker format={`${DATE_FORMAT} HH:mm`} showTime={{ format: "HH:mm", minuteStep: 15 }} />
                  </Form.Item>
                  <Button
                    danger
                    onClick={() => {
                      remove(field.name);
                    }}
                  >
                    Удалить
                  </Button>
                </Space>
              ))}
              <Button
                onClick={() => {
                  add({ period: undefined });
                }}
              >
                Добавить отпуск
              </Button>
            </Space>
          )}
        </Form.List>
      </Form>
    </DraftFormModal>
  );
}
