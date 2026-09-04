import type { FormInstance } from "antd";
import { Button, DatePicker, Form, Input, InputNumber, Space } from "antd";
import type { DefaultOptionType } from "antd/es/select";
import type dayjs from "dayjs";

import { ClientSelect } from "@/entities/client";
import { ServiceSelect } from "@/entities/service";
import { DATE_TIME_FORMAT, TIME_FORMAT } from "@/shared/lib";
import type { DurableFormStatus } from "@/shared/lib/react";
import { DraftFormModal } from "@/shared/ui";

import styles from "./PaymentCreateModal.module.css";

export type PaymentCreateFormValues = {
  clientId: string;
  serviceId?: string;
  quantity?: number;
  amount: number;
  date: dayjs.Dayjs | null;
  description?: string;
};

export function PaymentCreateModal({
  open,
  editing,
  hasDraft,
  draftRestored,
  draftSaveStatus,
  form,
  createPending,
  createdClientOptions,
  draftHydrationRef,
  selectedCreateServiceId,
  selectedServicePrice,
  onCancel,
  onClearDraft,
  draftStale,
  onReapplyDraft,
  onRetryDraft,
  onSubmit,
  onValuesChange,
  onCreateClient,
  onClientLabelChange,
  onServiceLabelChange,
  onServicePriceChange,
}: {
  open: boolean;
  editing: boolean;
  hasDraft: boolean;
  draftRestored: boolean;
  draftSaveStatus: DurableFormStatus;
  form: FormInstance<PaymentCreateFormValues>;
  createPending: boolean;
  createdClientOptions: DefaultOptionType[];
  draftHydrationRef: { current: boolean };
  selectedCreateServiceId?: string;
  selectedServicePrice?: number;
  onCancel: () => void;
  onClearDraft: () => void;
  draftStale: boolean;
  onReapplyDraft: () => void;
  onRetryDraft: () => void;
  onSubmit: (values: PaymentCreateFormValues) => void;
  onValuesChange: (changedValues: Partial<PaymentCreateFormValues>, values: PaymentCreateFormValues) => void;
  onCreateClient: () => void;
  onClientLabelChange: (label?: string) => void;
  onServiceLabelChange: (label?: string) => void;
  onServicePriceChange: (price?: number) => void;
}) {
  return (
    <DraftFormModal
      open={open}
      title={editing ? "Редактировать платеж" : "Новый платеж"}
      restored={!editing && draftRestored}
      saveStatus={draftSaveStatus}
      showClearDraft={hasDraft}
      showDraftState
      onClearDraft={onClearDraft}
      stale={draftStale}
      onReapplyDraft={onReapplyDraft}
      onRetryDraft={onRetryDraft}
      onCancel={onCancel}
      onOk={() => {
        form.submit();
      }}
      confirmLoading={createPending}
      destroyOnHidden
    >
      <Form
        form={form}
        layout="vertical"
        requiredMark={false}
        initialValues={{ quantity: 1 }}
        onFinish={onSubmit}
        onValuesChange={(changedValues, values) => {
          if (draftHydrationRef.current) {
            return;
          }

          const quantity = typeof values.quantity === "number" ? values.quantity : 1;
          let valuesToPersist = values;
          if (values.serviceId && selectedServicePrice !== undefined && ("serviceId" in changedValues || "quantity" in changedValues)) {
            const amount = selectedServicePrice * quantity;
            form.setFieldValue("amount", amount);
            valuesToPersist = { ...values, amount };
          }

          onValuesChange(changedValues, valuesToPersist);
        }}
      >
        <Form.Item label="Клиент">
          <Space orientation="vertical" size={8} className="wide">
            <Form.Item name="clientId" noStyle rules={[{ required: true }]}>
              <ClientSelect extraOptions={createdClientOptions} onResolvedLabelChange={onClientLabelChange} />
            </Form.Item>
            <Button onClick={onCreateClient}>Новый клиент</Button>
          </Space>
        </Form.Item>
        <div className={styles.serviceRow}>
          <Form.Item name="quantity" label="Кол-во" className={styles.quantityField}>
            <InputNumber min={1} precision={0} className="wide" disabled={!selectedCreateServiceId} />
          </Form.Item>
          <div style={{ display: "flex", alignItems: "center", paddingTop: 30 }}>x</div>
          <Form.Item name="serviceId" label="Услуга" className={styles.serviceField}>
            <ServiceSelect
              showPrice
              onResolvedLabelChange={onServiceLabelChange}
              onResolvedPriceChange={(price) => {
                onServicePriceChange(price);
                const { quantity, serviceId } = form.getFieldsValue(["quantity", "serviceId"]) as Pick<
                  PaymentCreateFormValues,
                  "quantity" | "serviceId"
                >;
                if (serviceId && price !== undefined) {
                  const amount = price * (typeof quantity === "number" ? quantity : 1);
                  form.setFieldValue("amount", amount);
                  onValuesChange({ amount }, { ...form.getFieldsValue(true), amount } as PaymentCreateFormValues);
                }
              }}
            />
          </Form.Item>
        </div>
        <Form.Item name="amount" label="Итого" rules={[{ required: true }]}>
          <InputNumber min={0} className="wide" disabled={Boolean(selectedCreateServiceId)} />
        </Form.Item>
        <Form.Item name="date" label="Дата" rules={[{ required: true }]}>
          <DatePicker showTime={{ format: TIME_FORMAT }} format={DATE_TIME_FORMAT} className="wide" />
        </Form.Item>
        <Form.Item name="description" label="Описание">
          <Input />
        </Form.Item>
      </Form>
    </DraftFormModal>
  );
}
