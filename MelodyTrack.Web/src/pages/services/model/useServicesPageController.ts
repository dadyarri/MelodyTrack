import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { App as AntdApp, Form } from "antd";
import { useEffect, useState } from "react";
import * as v from "valibot";

import { type Service, serviceQueryKeys, servicesApi } from "@/entities/service";
import { hasAdminAccess, useAuth } from "@/entities/session";
import { createIdempotencyKey, isShortcutTarget, matchesPlainKey } from "@/shared/lib";
import { jsonDurableFormCodec, readPositiveInteger, useDurableForm, useUrlState } from "@/shared/lib/react";

type ServiceDraftValues = {
  name?: string;
  publicName?: string;
  description?: string;
  isConsultation?: boolean;
  price?: number;
};

type ServiceCreateInput = {
  name: string;
  publicName?: string;
  description?: string;
  isConsultation: boolean;
  price: number;
};

type ServicePriceFormValues = {
  price: number;
};

type ServiceEditFormValues = {
  name: string;
  publicName?: string;
  description?: string;
  isConsultation: boolean;
};

const SERVICE_CREATE_DRAFT_KEY = "draft:services:create";
const serviceDraftSchema = v.object({
  name: v.optional(v.string()),
  publicName: v.optional(v.string()),
  description: v.optional(v.string()),
  isConsultation: v.optional(v.boolean()),
  price: v.optional(v.number()),
});
const serviceEditSchema = v.object({
  name: v.string(),
  publicName: v.optional(v.string()),
  description: v.optional(v.string()),
  isConsultation: v.boolean(),
});
const servicePriceSchema = v.object({ price: v.number() });
const serviceDraftCodec = jsonDurableFormCodec<ServiceDraftValues>();
const serviceEditCodec = jsonDurableFormCodec<ServiceEditFormValues>();
const servicePriceCodec = jsonDurableFormCodec<ServicePriceFormValues>();

export function useServicesPageController() {
  const { searchParams, setUrlState } = useUrlState();
  const auth = useAuth();
  const page = readPositiveInteger(searchParams.get("page"));
  const setPage = (nextPage: number) => {
    setUrlState({ page: nextPage === 1 ? null : nextPage });
  };
  const [isCreateRequestedOpen, setCreateOpen] = useState(false);
  const isCreateOpen = isCreateRequestedOpen;
  const [editing, setEditing] = useState<Service | null>(null);
  const [pricing, setPricing] = useState<Service | null>(null);
  const [form] = Form.useForm<ServiceDraftValues>();
  const [editForm] = Form.useForm<ServiceEditFormValues>();
  const [priceForm] = Form.useForm<ServicePriceFormValues>();
  const createDraft = useDurableForm({
    key: SERVICE_CREATE_DRAFT_KEY,
    schema: serviceDraftSchema,
    form,
    codec: serviceDraftCodec,
    enabled: isCreateOpen,
  });
  const editDraft = useDurableForm({
    key: editing ? `draft:services:edit:${editing.id}` : null,
    schema: serviceEditSchema,
    form: editForm,
    codec: serviceEditCodec,
    enabled: editing !== null,
    entity: editing ? { id: editing.id, baselineVersion: editing.lastActivity?.id ?? null } : undefined,
  });
  const priceDraft = useDurableForm({
    key: pricing ? `draft:services:price:${pricing.id}` : null,
    schema: servicePriceSchema,
    form: priceForm,
    codec: servicePriceCodec,
    enabled: pricing !== null,
    entity: pricing ? { id: pricing.id, baselineVersion: pricing.lastActivity?.id ?? null } : undefined,
  });
  const queryClient = useQueryClient();
  const { message } = AntdApp.useApp();
  const canManageServices = hasAdminAccess(auth.user);
  const query = useQuery({
    queryKey: serviceQueryKeys.list(page),
    queryFn: () => servicesApi.list({ page, page_size: 10 }),
    placeholderData: keepPreviousData,
  });

  const createMutation = useMutation({
    mutationFn: (values: ServiceCreateInput) => servicesApi.create(values, { idempotencyKey: createIdempotencyKey() }),
    onSuccess: async () => {
      message.success("Услуга создана");
      setCreateOpen(false);
      await createDraft.clearAfterSuccess();
      form.resetFields();
      await queryClient.invalidateQueries({ queryKey: serviceQueryKeys.all });
    },
  });

  const priceMutation = useMutation({
    mutationFn: ({ id, price }: { id: string; price: number }) => servicesApi.updatePrice(id, price),
    onSuccess: async () => {
      message.success("Цена обновлена");
      await priceDraft.clearAfterSuccess();
      setPricing(null);
      await queryClient.invalidateQueries({ queryKey: serviceQueryKeys.all });
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, values }: { id: string; values: ServiceEditFormValues }) => servicesApi.update(id, values),
    onSuccess: async () => {
      message.success("Услуга обновлена");
      await editDraft.clearAfterSuccess();
      setEditing(null);
      await queryClient.invalidateQueries({ queryKey: serviceQueryKeys.all });
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => servicesApi.remove(id),
    onSuccess: async () => {
      message.success("Услуга удалена");
      await queryClient.invalidateQueries({ queryKey: serviceQueryKeys.all });
    },
  });

  useEffect(() => {
    if (!editing) {
      editForm.resetFields();
      return;
    }

    editForm.setFieldsValue({
      name: editing.name,
      publicName: editing.publicName ?? undefined,
      description: editing.description ?? undefined,
      isConsultation: editing.isConsultation,
    });
  }, [editForm, editing]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.repeat || isShortcutTarget(event.target)) {
        return;
      }

      if (!matchesPlainKey(event, "a") || !canManageServices) {
        return;
      }

      event.preventDefault();
      setCreateOpen(true);
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      window.removeEventListener("keydown", handleKeyDown);
    };
  }, [canManageServices]);

  return {
    canManageServices,
    page,
    setPage,
    query,
    editing,
    setEditing,
    pricing,
    setPricing,
    hasCreateDraft: createDraft.hasDraft,
    isCreateDraftRestored: createDraft.restored,
    createDraftSaveStatus: createDraft.status,
    createDraftRetry: createDraft.retry,
    isCreateOpen,
    setCreateOpen,
    form,
    editForm,
    priceForm,
    createMutation,
    priceMutation,
    updateMutation,
    deleteMutation,
    handleClearCreateDraft: () => {
      void createDraft.discard().then(() => {
        form.resetFields();
      });
    },
    onCreateSubmit: (values: ServiceDraftValues) => {
      if (!values.name || values.price === undefined) {
        return;
      }

      createMutation.mutate({
        name: values.name,
        publicName: values.publicName,
        description: values.description,
        isConsultation: values.isConsultation ?? false,
        price: values.price,
      });
    },
    onCreateValuesChange: createDraft.formProps.onValuesChange,
    onEditValuesChange: editDraft.formProps.onValuesChange,
    onPriceValuesChange: priceDraft.formProps.onValuesChange,
    editDraft,
    priceDraft,
    onPriceSubmit: (values: ServicePriceFormValues) => {
      if (!pricing) {
        return;
      }

      priceMutation.mutate({ id: pricing.id, price: values.price });
    },
    onEditSubmit: (values: ServiceEditFormValues) => {
      if (!editing) {
        return;
      }

      updateMutation.mutate({ id: editing.id, values });
    },
  };
}
