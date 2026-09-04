import { useMutation, useQueryClient } from "@tanstack/react-query";
import { App as AntdApp, Form } from "antd";
import dayjs from "dayjs";
import { useCallback, useEffect, useState } from "react";
import * as v from "valibot";

import { clientQueryKeys } from "@/entities/client";
import { type Payment, paymentQueryKeys, paymentsApi } from "@/entities/payment";
import type { Ulid } from "@/shared/api";
import { getApiErrorMessages } from "@/shared/api";
import { useOpenCreateRouteIntent } from "@/shared/lib";
import { useCreatedReferenceOptions } from "@/shared/lib";
import { createIdempotencyKey } from "@/shared/lib";
import { findItemInQueryData, handleStaleEntityConflict, isActivityStale } from "@/shared/lib";
import { useDurableForm } from "@/shared/lib/react";

import type { PaymentCreateFormValues } from "../ui/PaymentCreateModal";

export type PaymentDraftValues = {
  clientId?: string;
  serviceId?: string;
  quantity?: number;
  amount?: number;
  date?: string;
  description?: string;
};

type PaymentCreatePrefill = {
  clientId?: string;
  serviceId?: string;
};

const PAYMENT_CREATE_DRAFT_KEY = "draft:payments:create";
const paymentDraftSchema = v.object({
  clientId: v.optional(v.string()),
  serviceId: v.optional(v.string()),
  quantity: v.optional(v.number()),
  amount: v.optional(v.number()),
  date: v.optional(v.string()),
  description: v.optional(v.string()),
});
const paymentDraftCodec = {
  serialize: (values: PaymentCreateFormValues): PaymentDraftValues => ({ ...values, date: values.date?.toISOString() }),
  deserialize: (values: PaymentDraftValues): Partial<PaymentCreateFormValues> => ({
    ...values,
    date: values.date ? dayjs(values.date) : undefined,
  }),
};

export function usePaymentCreateController({ useRouteIntent = false }: { useRouteIntent?: boolean } = {}) {
  const [isOpen, setOpen] = useState(false);
  const [localPrefill, setLocalPrefill] = useState<PaymentCreatePrefill | null>(null);
  const [editingPayment, setEditingPayment] = useState<Payment | null>(null);
  const [editingBaselineActivityId, setEditingBaselineActivityId] = useState<Ulid | null | undefined>();
  const [isQuickClientCreateOpen, setQuickClientCreateOpen] = useState(false);
  const createdClientOptions = useCreatedReferenceOptions("client");
  const [, setCreateClientLabel] = useState<string | undefined>();
  const [, setCreateServiceLabel] = useState<string | undefined>();
  const [selectedServicePrice, setSelectedServicePrice] = useState<number | undefined>();
  const [form] = Form.useForm<PaymentCreateFormValues>();
  const selectedCreateServiceId = Form.useWatch("serviceId", form);
  const selectedCreateQuantity = Form.useWatch("quantity", form) ?? 1;
  const routeIntent = useOpenCreateRouteIntent();
  const queryClient = useQueryClient();
  const { message, modal } = AntdApp.useApp();

  const showErrors = (error: unknown) => {
    for (const errorMessage of getApiErrorMessages(error)) {
      void message.error(errorMessage);
    }
  };

  const routePrefillClientId = useRouteIntent && routeIntent.hasOpenCreateIntent ? routeIntent.prefillClientId : undefined;
  const routePrefillServiceId = useRouteIntent && routeIntent.hasOpenCreateIntent ? routeIntent.prefillServiceId : undefined;
  const createPrefillClientId = localPrefill?.clientId ?? routePrefillClientId;
  const createPrefillServiceId = localPrefill?.serviceId ?? routePrefillServiceId;
  const isCreateModalOpen = isOpen || (useRouteIntent && routeIntent.hasOpenCreateIntent);
  const createDraft = useDurableForm({
    key: PAYMENT_CREATE_DRAFT_KEY,
    schema: paymentDraftSchema,
    form,
    codec: paymentDraftCodec,
    enabled: isCreateModalOpen && editingPayment === null,
  });
  const editDraft = useDurableForm({
    key: editingPayment ? `draft:payments:edit:${editingPayment.id}` : null,
    schema: paymentDraftSchema,
    form,
    codec: paymentDraftCodec,
    enabled: isCreateModalOpen && editingPayment !== null,
    entity: editingPayment ? { id: editingPayment.id, baselineVersion: editingBaselineActivityId ?? null } : undefined,
  });
  const currentEditingPayment = editingPayment;
  const isEditingPaymentStale = currentEditingPayment
    ? isActivityStale(currentEditingPayment.lastActivity?.id, editingBaselineActivityId)
    : false;

  const saveMutation = useMutation<unknown, unknown, { values: PaymentCreateFormValues; expectedActivityId?: Ulid }>({
    mutationFn: ({ values, expectedActivityId }) => {
      if (!values.date) {
        throw new Error("Укажите дату платежа.");
      }

      const input = {
        clientId: values.clientId,
        serviceId: values.serviceId,
        amount: values.amount,
        date: values.date.toISOString(),
        description: values.description?.trim() || undefined,
      };

      if (editingPayment) {
        return paymentsApi.update(editingPayment.id, input, { expectedActivityId });
      }

      return paymentsApi.create(input, { idempotencyKey: createIdempotencyKey() }).then(() => undefined);
    },
    onSuccess: async () => {
      message.success("Платеж сохранен");
      await (editingPayment ? editDraft : createDraft).clearAfterSuccess();
      closeCreateModal();
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: paymentQueryKeys.all }),
        queryClient.invalidateQueries({ queryKey: clientQueryKeys.all }),
        queryClient.invalidateQueries({ queryKey: clientQueryKeys.history() }),
      ]);
    },
    meta: { suppressErrorNotification: true },
    onError: async (error, variables) => {
      if (!editingPayment) {
        showErrors(error);
        return;
      }

      await handleStaleEntityConflict({
        error,
        modal,
        queryClient,
        invalidateQueryKey: paymentQueryKeys.all,
        showErrors,
        title: "Платеж уже изменен",
        okText: "Перезаписать",
        cancelText: "Обновить форму",
        onConfirm: (conflict) => {
          saveMutation.mutate({
            values: variables.values,
            expectedActivityId: conflict.currentActivity?.id,
          });
        },
        onReload: () => {
          const freshPayment =
            findItemInQueryData(
              queryClient,
              paymentQueryKeys.all,
              (data) => (data as { data: Payment[] } | undefined)?.data,
              editingPayment.id,
            ) ?? currentEditingPayment;

          if (!freshPayment) {
            return;
          }

          setEditingPayment(freshPayment);
          setEditingBaselineActivityId(freshPayment.lastActivity?.id ?? null);
          setSelectedServicePrice(undefined);
          form.setFieldsValue({
            clientId: freshPayment.client.id,
            serviceId: freshPayment.service?.id,
            quantity: 1,
            amount: freshPayment.amount,
            date: dayjs(freshPayment.date),
            description: freshPayment.description ?? undefined,
          });
        },
      });
    },
  });

  const openCreateModal = useCallback((prefill?: PaymentCreatePrefill) => {
    setEditingPayment(null);
    setEditingBaselineActivityId(undefined);
    setLocalPrefill(prefill ?? null);
    setOpen(true);
  }, []);

  const openEditModal = useCallback(
    (payment: Payment) => {
      setEditingPayment(payment);
      setEditingBaselineActivityId(payment.lastActivity?.id ?? null);
      setLocalPrefill(null);
      setSelectedServicePrice(undefined);
      setOpen(true);
      form.setFieldsValue({
        clientId: payment.client.id,
        serviceId: payment.service?.id,
        quantity: 1,
        amount: payment.amount,
        date: dayjs(payment.date),
        description: payment.description ?? undefined,
      });
    },
    [form],
  );

  useEffect(() => {
    if (!isCreateModalOpen) {
      return;
    }

    if (editingPayment) {
      return;
    }

    form.setFieldsValue({
      clientId: createPrefillClientId,
      serviceId: createPrefillServiceId,
      quantity: 1,
      amount: undefined,
      date: dayjs(),
      description: undefined,
    });
  }, [createPrefillClientId, createPrefillServiceId, editingPayment, form, isCreateModalOpen]);

  useEffect(() => {
    if (!selectedCreateServiceId || selectedServicePrice === undefined || createDraft.isHydratingRef.current) {
      return;
    }

    form.setFieldValue("amount", selectedServicePrice * selectedCreateQuantity);
  }, [createDraft.isHydratingRef, form, selectedCreateQuantity, selectedCreateServiceId, selectedServicePrice]);

  function closeCreateModal() {
    setOpen(false);
    setLocalPrefill(null);
    setEditingPayment(null);
    setEditingBaselineActivityId(undefined);
    form.setFieldsValue({
      clientId: undefined,
      serviceId: undefined,
      quantity: 1,
      amount: undefined,
      date: dayjs(),
      description: undefined,
    });
    setSelectedServicePrice(undefined);
    if (useRouteIntent) {
      routeIntent.clearOpenCreateIntent();
    }
  }

  function handleClearCreateDraft() {
    void (editingPayment ? editDraft : createDraft).discard().then(() => {
      form.setFieldsValue({
        clientId: editingPayment?.client.id ?? createPrefillClientId,
        serviceId: editingPayment?.service?.id ?? createPrefillServiceId,
        quantity: 1,
        amount: editingPayment?.amount,
        date: editingPayment ? dayjs(editingPayment.date) : dayjs(),
        description: editingPayment?.description ?? undefined,
      });
    });
    setSelectedServicePrice(undefined);
  }

  return {
    form,
    draftHydrationRef: createDraft.isHydratingRef,
    selectedCreateServiceId,
    selectedServicePrice,
    setSelectedServicePrice,
    createdClientOptions: createdClientOptions.createdOptions,
    hasCreateDraft: (editingPayment ? editDraft : createDraft).hasDraft,
    isCreateDraftRestored: (editingPayment ? editDraft : createDraft).restored,
    createDraftSaveStatus: (editingPayment ? editDraft : createDraft).status,
    activeDraft: editingPayment ? editDraft : createDraft,
    isCreateModalOpen,
    editingPayment,
    currentEditingPayment,
    isEditingPaymentStale: isEditingPaymentStale || editDraft.isStale,
    editingBaselineActivityId,
    isQuickClientCreateOpen,
    setQuickClientCreateOpen,
    saveMutation,
    openCreateModal,
    openEditModal,
    closeCreateModal,
    handleClearCreateDraft,
    setCreateClientLabel,
    setCreateServiceLabel,
    onCreateValuesChange: (_: Partial<PaymentCreateFormValues>, values: PaymentCreateFormValues) => {
      (editingPayment ? editDraft : createDraft).formProps.onValuesChange?.(_, values);
    },
    onQuickClientCreated: (client: { id: string; displayName: string }) => {
      createdClientOptions.addCreatedOption({
        id: client.id,
        label: client.displayName,
      });
      setCreateClientLabel(client.displayName);
      form.setFieldValue("clientId", client.id);
      setQuickClientCreateOpen(false);
    },
  };
}
