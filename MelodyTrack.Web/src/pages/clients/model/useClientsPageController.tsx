import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { App as AntdApp, Form } from "antd";
import dayjs from "dayjs";
import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router";
import * as v from "valibot";

import { type Client, clientQueryKeys, clientsApi, getClientContactValue, normalizePhone, normalizeSocialLink } from "@/entities/client";
import type { CourseEnrollment, CourseEnrollmentThemeProgressAction } from "@/entities/course";
import { courseEnrollmentsApi, courseQueryKeys, coursesApi } from "@/entities/course";
import { clientSourcesApi } from "@/entities/reference-book";
import { hasAdminAccess, useAuth } from "@/entities/session";
import type { ClientFormValues, ClientVacationsFormValues } from "@/features/manage-client";
import { useUpdateCourseProgress } from "@/features/update-course-progress";
import { getApiErrorMessages, type Ulid } from "@/shared/api";
import { createIdempotencyKey, useCreatedReferenceOptions } from "@/shared/lib";
import { getBackgroundRefetchInterval } from "@/shared/lib";
import { isShortcutTarget, matchesPlainKey } from "@/shared/lib";
import { findItemInQueryData, handleStaleEntityConflict, isActivityStale } from "@/shared/lib";
import { readPositiveInteger, useDurableForm, useUrlState } from "@/shared/lib/react";
import { useUrlCopyModal } from "@/shared/ui";
import { getClientHistoryActions } from "@/widgets/client-history";

type ClientSubmitInput = {
  firstName: string;
  lastName: string;
  patronymic?: string | null;
  dateOfBirth?: string | null;
  telegram?: string;
  vk?: string;
  phone?: string;
  sourceId?: string;
  vacations?: Array<{ startDate: string; endDate: string }>;
};

type ClientDraftValues = {
  firstName?: string;
  lastName?: string;
  patronymic?: string | null;
  dateOfBirth?: string | null;
  telegram?: string | null;
  vk?: string | null;
  phone?: string | null;
  sourceId?: string;
};

const CLIENT_CREATE_DRAFT_KEY = "draft:clients:create";
const clientDraftSchema = v.object({
  firstName: v.optional(v.string()),
  lastName: v.optional(v.string()),
  patronymic: v.optional(v.nullable(v.string())),
  dateOfBirth: v.optional(v.nullable(v.string())),
  telegram: v.optional(v.nullable(v.string())),
  vk: v.optional(v.nullable(v.string())),
  phone: v.optional(v.nullable(v.string())),
  sourceId: v.optional(v.string()),
});
const clientDraftCodec = {
  serialize: (values: ClientFormValues): ClientDraftValues => ({
    firstName: values.firstName,
    lastName: values.lastName,
    patronymic: values.patronymic,
    dateOfBirth: values.dateOfBirth,
    telegram: values.telegram,
    vk: values.vk,
    phone: values.phone,
    sourceId: values.sourceId ?? undefined,
  }),
  deserialize: (values: ClientDraftValues): Partial<ClientFormValues> => values,
};
type ClientVacationsDraftValues = { vacations?: Array<{ period?: [string, string] }> };
const clientVacationsDraftSchema = v.object({
  vacations: v.optional(v.array(v.object({ period: v.optional(v.tuple([v.string(), v.string()])) }))),
});
const clientVacationsDraftCodec = {
  serialize: (values: ClientVacationsFormValues): ClientVacationsDraftValues => ({
    vacations: values.vacations?.map((vacation) => ({
      period: vacation.period ? [vacation.period[0].toISOString(), vacation.period[1].toISOString()] : undefined,
    })),
  }),
  deserialize: (values: ClientVacationsDraftValues): Partial<ClientVacationsFormValues> => ({
    vacations: values.vacations?.map((vacation) => ({
      period: vacation.period ? [dayjs(vacation.period[0]), dayjs(vacation.period[1])] : undefined,
    })),
  }),
};
const clientHistoryEventsPageSize = 8;

export function useClientsPageController() {
  const { searchParams, setUrlState } = useUrlState();
  const page = readPositiveInteger(searchParams.get("page"));
  const search = searchParams.get("q") ?? "";
  const setPage = useCallback(
    (nextPage: number) => {
      setUrlState({ page: nextPage === 1 ? null : nextPage });
    },
    [setUrlState],
  );
  const [editing, setEditing] = useState<Client | null>(null);
  const [editingBaselineActivityId, setEditingBaselineActivityId] = useState<Ulid | null | undefined>();
  const [isCreateRequestedOpen, setCreateOpen] = useState(false);
  const isCreateOpen = isCreateRequestedOpen;
  const [historyClient, setHistoryClient] = useState<Client | null>(null);
  const [historyEventsPage, setHistoryEventsPage] = useState(1);
  const [isSourceCreateOpen, setSourceCreateOpen] = useState(false);
  const [isEnrollmentCreateOpen, setEnrollmentCreateOpen] = useState(false);
  const createdSourceOptions = useCreatedReferenceOptions("client-source");
  const [form] = Form.useForm<ClientFormValues>();
  const [vacationsForm] = Form.useForm<ClientVacationsFormValues>();
  const [vacationsClient, setVacationsClient] = useState<Client | null>(null);
  const createDraft = useDurableForm({
    key: CLIENT_CREATE_DRAFT_KEY,
    schema: clientDraftSchema,
    form,
    codec: clientDraftCodec,
    enabled: isCreateOpen && editing === null,
  });
  const editDraft = useDurableForm({
    key: editing ? `draft:clients:edit:${editing.id}` : null,
    schema: clientDraftSchema,
    form,
    codec: clientDraftCodec,
    enabled: isCreateOpen && editing !== null,
    entity: editing ? { id: editing.id, baselineVersion: editingBaselineActivityId ?? null } : undefined,
  });
  const vacationsDraft = useDurableForm({
    key: vacationsClient ? `draft:clients:vacations:${vacationsClient.id}` : null,
    schema: clientVacationsDraftSchema,
    form: vacationsForm,
    codec: clientVacationsDraftCodec,
    enabled: vacationsClient !== null,
    entity: vacationsClient ? { id: vacationsClient.id, baselineVersion: vacationsClient.lastActivity?.id ?? null } : undefined,
  });
  const navigate = useNavigate();
  const auth = useAuth();
  const urlModal = useUrlCopyModal(auth.user?.id);
  const queryClient = useQueryClient();
  const { message, modal } = AntdApp.useApp();

  const showErrors = (error: unknown) => {
    for (const errorMessage of getApiErrorMessages(error)) {
      void message.error(errorMessage);
    }
  };

  const canCreateClients = hasAdminAccess(auth.user);
  const historyClientId = historyClient?.id;

  const query = useQuery({
    queryKey: clientQueryKeys.list(page, search),
    queryFn: () =>
      clientsApi.list({
        page,
        page_size: 10,
        search: search.trim() || undefined,
      }),
    placeholderData: keepPreviousData,
    refetchInterval: getBackgroundRefetchInterval(
      isCreateOpen || Boolean(editing || historyClient || vacationsClient) || isSourceCreateOpen || isEnrollmentCreateOpen,
    ),
  });

  const historyQuery = useQuery({
    queryKey: clientQueryKeys.history(historyClientId, historyEventsPage, clientHistoryEventsPageSize),
    queryFn: () => {
      if (!historyClientId) {
        throw new Error("History client is not selected.");
      }

      return clientsApi.history(historyClientId, {
        page: historyEventsPage,
        page_size: clientHistoryEventsPageSize,
      });
    },
    enabled: Boolean(historyClientId),
    placeholderData: keepPreviousData,
  });

  const courseEnrollmentsQuery = useQuery({
    queryKey: courseQueryKeys.enrollments.list({ clientId: historyClientId }),
    queryFn: () => {
      if (!historyClientId) {
        throw new Error("History client is not selected.");
      }

      return courseEnrollmentsApi.list({ clientId: historyClientId });
    },
    enabled: Boolean(historyClientId),
  });

  const coursesCatalogQuery = useQuery({
    queryKey: courseQueryKeys.list(""),
    queryFn: () => coursesApi.list(),
    enabled: Boolean(historyClient),
    staleTime: 60_000,
  });

  const currentEditingClient = editing ? (query.data?.data.find((client) => client.id === editing.id) ?? editing) : null;
  const isEditingClientStale = currentEditingClient
    ? isActivityStale(currentEditingClient.lastActivity?.id, editingBaselineActivityId)
    : false;
  const hasStaleClientDraft = editDraft.isStale;

  const saveMutation = useMutation<unknown, unknown, { values: ClientFormValues; expectedActivityId?: Ulid }>({
    mutationFn: ({ values, expectedActivityId }) => {
      const input = prepareClientInput(values);
      if (editing) {
        return clientsApi.update(editing.id, input, { expectedActivityId });
      }

      return clientsApi.create(input, { idempotencyKey: createIdempotencyKey() }).then(() => undefined);
    },
    onSuccess: async () => {
      message.success("Клиент сохранен");
      await (editing ? editDraft : createDraft).clearAfterSuccess();
      setCreateOpen(false);
      setEditing(null);
      setEditingBaselineActivityId(undefined);
      form.resetFields();
      await queryClient.invalidateQueries({ queryKey: clientQueryKeys.all });
    },
    onError: async (error, variables) => {
      if (!editing) {
        showErrors(error);
        return;
      }

      await handleStaleEntityConflict({
        error,
        modal,
        queryClient,
        invalidateQueryKey: clientQueryKeys.all,
        showErrors,
        title: "Клиент уже изменен",
        okText: "Перезаписать",
        cancelText: "Обновить форму",
        onConfirm: (conflict) => {
          saveMutation.mutate({
            values: variables.values,
            expectedActivityId: conflict.currentActivity?.id,
          });
        },
        onReload: () => {
          const freshClient =
            findItemInQueryData(queryClient, clientQueryKeys.all, (data) => (data as { data: Client[] } | undefined)?.data, editing.id) ??
            currentEditingClient;
          if (!freshClient) {
            return;
          }

          setEditing(freshClient);
          setEditingBaselineActivityId(freshClient.lastActivity?.id ?? null);
          form.setFieldsValue({
            ...freshClient,
            telegram: getClientContactValue(freshClient, "telegram"),
            vk: getClientContactValue(freshClient, "vk"),
            phone: getClientContactValue(freshClient, "phone"),
          });
        },
      });
    },
  });

  const vacationsMutation = useMutation({
    mutationFn: ({ client, values }: { client: Client; values: ClientVacationsFormValues }) =>
      clientsApi.update(client.id, prepareClientVacationUpdate(client, values), { expectedActivityId: client.lastActivity?.id }),
    onSuccess: async () => {
      message.success("Периоды отсутствия сохранены");
      await vacationsDraft.clearAfterSuccess();
      setVacationsClient(null);
      vacationsForm.resetFields();
      await queryClient.invalidateQueries({ queryKey: clientQueryKeys.all });
    },
    onError: showErrors,
  });

  const deleteMutation = useMutation({
    mutationFn: ({ id, expectedActivityId }: { id: Ulid; expectedActivityId?: Ulid }) => {
      return clientsApi.remove(id, { expectedActivityId });
    },
    onSuccess: async () => {
      message.success("Клиент удален");
      await queryClient.invalidateQueries({ queryKey: clientQueryKeys.all });
    },
    onError: async (error, variables) => {
      await handleStaleEntityConflict({
        error,
        modal,
        queryClient,
        invalidateQueryKey: clientQueryKeys.all,
        showErrors,
        title: "Клиент уже изменен",
        okText: "Удалить все равно",
        cancelText: "Обновить список",
        onConfirm: (conflict) => {
          deleteMutation.mutate({
            id: variables.id,
            expectedActivityId: conflict.currentActivity?.id,
          });
        },
        onReload: () => {
          void queryClient.invalidateQueries({
            queryKey: clientQueryKeys.all,
          });
        },
      });
    },
  });

  const leadStatusMutation = useMutation({
    mutationFn: ({ id, isClosed }: { id: Ulid; isClosed: boolean }) => clientsApi.setLeadClosed(id, isClosed),
    onSuccess: async (_result, variables) => {
      message.success(variables.isClosed ? "Лид закрыт" : "Лид возвращен в работу");
      await queryClient.invalidateQueries({ queryKey: clientQueryKeys.all });
    },
    onError: showErrors,
  });

  const createPortalLinkMutation = useMutation({
    mutationFn: (clientId: Ulid) => clientsApi.createPortalLink(clientId),
    onSuccess: (payload) => {
      urlModal.openUrlModal({
        url: payload.url,
        title: "Ссылка на портал клиента",
        description: "Скопируйте ссылку и отправьте её клиенту.",
        warning: "Предыдущая ссылка уже отключена, а активные сессии клиента завершены.",
      });
      message.success("Новая ссылка на портал создана");
    },
    onError: showErrors,
  });

  const revokePortalLinkMutation = useMutation({
    mutationFn: (clientId: Ulid) => clientsApi.revokePortalLink(clientId),
    onSuccess: () => {
      message.success("Ссылка клиентского кабинета отключена");
    },
    onError: showErrors,
  });

  const createCalendarSubscriptionMutation = useMutation({
    mutationFn: (clientId: Ulid) => clientsApi.regenerateCalendarSubscription(clientId),
    onSuccess: (subscription) => {
      urlModal.openUrlModal({
        url: subscription.url,
        title: "Календарь клиента",
        description: "Скопируйте ссылку и передайте её клиенту для добавления в календарь.",
        warning: "Предыдущая ссылка на календарь уже отключена.",
      });
      message.success("Ссылка на календарь создана");
    },
    onError: showErrors,
  });

  const resetPortalPinMutation = useMutation({
    mutationFn: (clientId: Ulid) => clientsApi.resetPortalPin(clientId),
    onSuccess: () => {
      message.success("PIN клиентского кабинета сброшен");
    },
    onError: showErrors,
  });

  const openEditor = (client?: Client) => {
    if (client) {
      setEditing(client);
      setEditingBaselineActivityId(client.lastActivity?.id ?? null);
      setCreateOpen(true);
      form.resetFields();
      form.setFieldsValue({
        ...client,
        telegram: getClientContactValue(client, "telegram"),
        vk: getClientContactValue(client, "vk"),
        phone: getClientContactValue(client, "phone"),
      });
      return;
    }

    if (!canCreateClients) {
      return;
    }

    setEditing(null);
    setEditingBaselineActivityId(undefined);
    setCreateOpen(true);
    form.resetFields();
  };

  const createSourceMutation = useMutation({
    mutationFn: (values: { name: string }) => clientSourcesApi.create(values),
    onSuccess: async (result, values) => {
      message.success("Источник создан");
      createdSourceOptions.addCreatedOption({
        id: result.id,
        label: values.name.trim(),
      });
      form.setFieldValue("sourceId", result.id);
      setSourceCreateOpen(false);
      await queryClient.invalidateQueries({
        queryKey: clientQueryKeys.sources,
      });
    },
    onError: showErrors,
  });

  const createEnrollmentMutation = useMutation({
    mutationFn: ({ courseId }: { courseId: Ulid; openProgress: boolean }) => {
      if (!historyClient) {
        throw new Error("Клиент не выбран.");
      }

      return courseEnrollmentsApi.create({ clientId: historyClient.id, courseId });
    },
    onSuccess: async () => {
      message.success("Курс назначен");
      setEnrollmentCreateOpen(false);
      await queryClient.invalidateQueries({ queryKey: courseQueryKeys.enrollments.all });
    },
    onError: showErrors,
  });

  const deleteEnrollmentMutation = useMutation({
    mutationFn: (enrollmentId: Ulid) => courseEnrollmentsApi.remove(enrollmentId),
    onSuccess: async () => {
      message.success("Курс снят с клиента");
      await queryClient.invalidateQueries({ queryKey: courseQueryKeys.enrollments.all });
    },
    onError: showErrors,
  });

  const updateThemeProgressMutation = useUpdateCourseProgress({ onError: showErrors });

  const clientHistoryActions = getClientHistoryActions(auth.user, navigate);

  const handleSearch = useCallback(
    (value: string) => {
      setUrlState({ page: null, q: value.trim() || null });
    },
    [setUrlState],
  );

  const openHistoryClient = (client: Client) => {
    setHistoryEventsPage(1);
    setHistoryClient(client);
  };

  const closeHistoryClient = () => {
    setHistoryClient(null);
    setHistoryEventsPage(1);
    setEnrollmentCreateOpen(false);
  };

  const handleClearCreateDraft = () => {
    const activeDraft = editing ? editDraft : createDraft;
    void activeDraft.discard().then(() => {
      form.resetFields();
      if (editing) {
        form.setFieldsValue({
          ...editing,
          telegram: getClientContactValue(editing, "telegram"),
          vk: getClientContactValue(editing, "vk"),
          phone: getClientContactValue(editing, "phone"),
        });
      }
    });
  };

  const closeEditor = () => {
    setCreateOpen(false);
    setEditing(null);
    setEditingBaselineActivityId(undefined);
  };

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.repeat || isShortcutTarget(event.target)) {
        return;
      }

      if (matchesPlainKey(event, "a")) {
        if (!canCreateClients) {
          return;
        }

        event.preventDefault();
        setEditing(null);
        setEditingBaselineActivityId(undefined);
        setCreateOpen(true);
        form.resetFields();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      window.removeEventListener("keydown", handleKeyDown);
    };
  }, [canCreateClients, form]);

  return {
    canCreateClients,
    page,
    setPage,
    search,
    query,
    clients: query.data?.data,
    pagination: {
      current: query.data?.info.page ?? page,
      pageSize: query.data?.info.pageSize ?? 10,
      total: query.data?.info.total,
    },
    historyQuery,
    historyClient,
    historyEventsPage,
    courseEnrollmentsQuery,
    coursesCatalogQuery,
    setHistoryEventsPage,
    setHistoryClient: openHistoryClient,
    closeHistoryClient,
    vacationsForm,
    vacationsClient,
    vacationsDraft,
    editing,
    isCreateOpen,
    hasCreateDraft: (editing ? editDraft : createDraft).hasDraft,
    isCreateDraftRestored: (editing ? editDraft : createDraft).restored,
    createDraftSaveStatus: (editing ? editDraft : createDraft).status,
    editorDraft: editing ? editDraft : createDraft,
    form,
    currentEditingClient,
    isEditingClientStale: isEditingClientStale || hasStaleClientDraft,
    editingBaselineActivityId,
    isSourceCreateOpen,
    isEnrollmentCreateOpen,
    createdSourceOptions: createdSourceOptions.createdOptions,
    saveMutation,
    vacationsMutation,
    createSourceMutation,
    createEnrollmentMutation,
    deleteEnrollmentMutation,
    updateThemeProgressMutation,
    deleteMutation,
    leadStatusMutation,
    createPortalLinkMutation,
    revokePortalLinkMutation,
    createCalendarSubscriptionMutation,
    resetPortalPinMutation,
    urlModalProps: urlModal.urlModalProps,
    openEditor,
    closeEditor,
    handleSearch,
    handleClearCreateDraft,
    onSubmit: (values: ClientFormValues) => {
      saveMutation.mutate({
        values,
        expectedActivityId: editingBaselineActivityId ?? undefined,
      });
    },
    onValuesChange: (_: Partial<ClientFormValues>, values: ClientFormValues) => {
      (editing ? editDraft : createDraft).formProps.onValuesChange?.(_, values);
    },
    clientHistoryActions,
    openVacationsEditor: (client: Client) => {
      vacationsForm.setFieldsValue({
        vacations: client.vacations.map((vacation) => ({ period: [dayjs(vacation.startDate), dayjs(vacation.endDate)] })),
      });
      setVacationsClient(client);
    },
    closeVacationsEditor: () => {
      setVacationsClient(null);
      vacationsForm.resetFields();
    },
    saveVacations: (values: ClientVacationsFormValues) => {
      if (vacationsClient) {
        vacationsMutation.mutate({ client: vacationsClient, values });
      }
    },
    confirmDelete: (client: Client) => {
      modal.confirm({
        title: "Удалить клиента?",
        onOk: () => {
          deleteMutation.mutate({
            id: client.id,
            expectedActivityId: client.lastActivity?.id,
          });
        },
      });
    },
    setLeadClosed: (client: Client, isClosed: boolean) => {
      leadStatusMutation.mutate({ id: client.id, isClosed });
    },
    openSourceCreate: () => {
      if (!canCreateClients) {
        return;
      }

      setSourceCreateOpen(true);
    },
    closeSourceCreate: () => {
      setSourceCreateOpen(false);
    },
    openEnrollmentCreate: () => {
      if (historyClient && canCreateClients) {
        setEnrollmentCreateOpen(true);
      }
    },
    closeEnrollmentCreate: () => {
      setEnrollmentCreateOpen(false);
    },
    availableEnrollmentCourses: buildAvailableEnrollmentCourses(coursesCatalogQuery.data ?? [], courseEnrollmentsQuery.data ?? []),
    onCreateSource: (values: { name: string }) => {
      createSourceMutation.mutate(values);
    },
    onCreateEnrollment: (values: { courseId: Ulid; openProgress: boolean }) => {
      createEnrollmentMutation.mutate(values);
    },
    onCreatePortalLink: () => {
      if (!historyClient) {
        return;
      }

      modal.confirm({
        title: "Создать новую ссылку на портал?",
        content: "Предыдущая ссылка перестанет работать, а активные сессии клиента будут завершены.",
        okText: "Создать ссылку",
        onOk: () => {
          createPortalLinkMutation.mutate(historyClient.id);
        },
      });
    },
    onRevokePortalLink: () => {
      if (!historyClient) {
        return;
      }

      modal.confirm({
        title: "Отключить ссылку на портал?",
        content: "Ссылка перестанет работать, а активные сессии клиента будут завершены.",
        okText: "Отключить",
        okButtonProps: { danger: true },
        onOk: () => {
          revokePortalLinkMutation.mutate(historyClient.id);
        },
      });
    },
    onCreateCalendarSubscription: () => {
      if (historyClient) {
        createCalendarSubscriptionMutation.mutate(historyClient.id);
      }
    },
    onResetPortalPin: () => {
      if (!historyClient) {
        return;
      }

      modal.confirm({
        title: "Сбросить PIN клиентского кабинета?",
        content: "Текущий PIN перестанет работать, а активные сессии клиента будут завершены.",
        okText: "Сбросить PIN",
        okButtonProps: { danger: true },
        onOk: () => {
          resetPortalPinMutation.mutate(historyClient.id);
        },
      });
    },
    onDeleteEnrollment: (enrollmentId: Ulid) => {
      modal.confirm({
        title: "Снять клиента с курса?",
        onOk: () => {
          deleteEnrollmentMutation.mutate(enrollmentId);
        },
      });
    },
    openCourseProgress: (enrollmentId?: Ulid) => {
      const enrollment = (courseEnrollmentsQuery.data ?? []).find((item) => item.id === enrollmentId) ?? courseEnrollmentsQuery.data?.[0];
      if (enrollment) {
        void navigate(`/courses?${new URLSearchParams({ course: enrollment.courseId, enrollment: enrollment.id }).toString()}`);
      }
    },
    onUpdateThemeProgress: (themeId: Ulid, action: CourseEnrollmentThemeProgressAction) => {
      updateThemeProgressMutation.mutate({ themeId, action });
    },
    onSourceLabelChange: (_label?: string) => {},
  };
}

function buildAvailableEnrollmentCourses(
  courses: Array<{ id: Ulid; name: string; description?: string | null; blockCount: number; themeCount: number }>,
  enrollments: CourseEnrollment[],
) {
  const enrolledCourseIds = new Set(enrollments.map((enrollment) => enrollment.courseId));
  return courses
    .filter((course) => !enrolledCourseIds.has(course.id))
    .map((course) => ({
      value: course.id,
      label: course.name,
      description: course.description,
      blockCount: course.blockCount,
      themeCount: course.themeCount,
    }));
}

function prepareClientInput(values: ClientFormValues): ClientSubmitInput {
  const input: ClientSubmitInput = {
    firstName: values.firstName,
    lastName: values.lastName,
    patronymic: values.patronymic,
    dateOfBirth: values.dateOfBirth || undefined,
    phone: normalizePhone(values.phone),
    telegram: normalizeSocialLink(values.telegram, "telegram"),
    vk: normalizeSocialLink(values.vk, "vk"),
    sourceId: values.sourceId ?? undefined,
  };

  return omitEmptyContacts(input);
}

function prepareClientVacationUpdate(client: Client, values: ClientVacationsFormValues) {
  return {
    firstName: client.firstName,
    lastName: client.lastName,
    patronymic: client.patronymic,
    dateOfBirth: client.dateOfBirth,
    phone: getClientContactValue(client, "phone"),
    telegram: getClientContactValue(client, "telegram"),
    vk: getClientContactValue(client, "vk"),
    sourceId: client.sourceId,
    vacations: (values.vacations ?? []).flatMap((vacation) =>
      vacation.period ? [{ startDate: vacation.period[0].format("YYYY-MM-DD"), endDate: vacation.period[1].format("YYYY-MM-DD") }] : [],
    ),
  };
}

function omitEmptyContacts(input: ClientSubmitInput) {
  const result = { ...input };
  if (!result.phone) {
    delete result.phone;
  }
  if (!result.telegram) {
    delete result.telegram;
  }
  if (!result.vk) {
    delete result.vk;
  }

  return result;
}
