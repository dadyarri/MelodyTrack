import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { App as AntdApp, Form } from "antd";
import type { DefaultOptionType } from "antd/es/select";
import dayjs, { type Dayjs } from "dayjs";
import { useCallback, useEffect, useRef, useState } from "react";
import * as v from "valibot";

import {
  type Appointment,
  type AppointmentMutationScope,
  appointmentQueryKeys,
  appointmentsApi,
  type AppointmentStatus,
  type RecurrenceType,
} from "@/entities/appointment";
import { type CourseEnrollment, courseEnrollmentsApi, courseQueryKeys, type CourseThemeProgressState } from "@/entities/course";
import { hasAdminAccess, hasSuperuserAccess, useAuth } from "@/entities/session";
import { userQueryKeys, usersApi } from "@/entities/user";
import { getVisibleScheduleHours } from "@/entities/user";
import type { AppointmentEditFormValues, AppointmentFormValues } from "@/features/manage-appointment";
import { type VacationRangeFormValues, vacationRequestQueryKeys, vacationRequestsApi } from "@/features/manage-vacation-requests";
import { usePaymentCreateController } from "@/features/record-payment";
import { getApiErrorMessages, type Ulid } from "@/shared/api";
import { useOpenCreateRouteIntent } from "@/shared/lib";
import { useCreatedReferenceOptions } from "@/shared/lib";
import { createIdempotencyKey } from "@/shared/lib";
import { getBackgroundRefetchInterval } from "@/shared/lib";
import { isShortcutTarget, matchesPlainKey } from "@/shared/lib";
import { findItemInQueryData, handleStaleEntityConflict, isActivityStale } from "@/shared/lib";
import { useDurableForm, useUrlState } from "@/shared/lib/react";

const timezone = Intl.DateTimeFormat().resolvedOptions().timeZone;
const APPOINTMENT_CREATE_DRAFT_KEY = "draft:appointments:create";

export type AppointmentDraftValues = {
  clientId?: string;
  serviceId?: string;
  providerId?: string;
  courseThemeId?: string;
  lessonNotes?: string;
  startDate?: string;
  recurrenceTypeId?: string;
  patternEndDate?: string;
  weeklyDays?: number[];
};
const appointmentDraftSchema = v.object({
  clientId: v.optional(v.string()),
  serviceId: v.optional(v.string()),
  providerId: v.optional(v.string()),
  courseThemeId: v.optional(v.string()),
  lessonNotes: v.optional(v.string()),
  startDate: v.optional(v.string()),
  recurrenceTypeId: v.optional(v.string()),
  patternEndDate: v.optional(v.string()),
  weeklyDays: v.optional(v.array(v.number())),
});
const appointmentDraftCodec = {
  serialize: serializeAppointmentDraft,
  deserialize: (values: AppointmentDraftValues): Partial<AppointmentFormValues> => ({
    ...values,
    startDate: values.startDate ? dayjs(values.startDate) : null,
    patternEndDate: values.patternEndDate ? dayjs(values.patternEndDate) : undefined,
  }),
};
const appointmentEditDraftSchema = v.object({
  clientId: v.string(),
  serviceId: v.string(),
  providerId: v.optional(v.string()),
  courseThemeId: v.optional(v.string()),
  lessonNotes: v.optional(v.string()),
  startDate: v.string(),
});
type AppointmentEditDraftValues = Omit<AppointmentEditFormValues, "startDate"> & { startDate: string };
const appointmentEditDraftCodec = {
  serialize: (values: AppointmentEditFormValues): AppointmentEditDraftValues => ({ ...values, startDate: values.startDate.toISOString() }),
  deserialize: (values: AppointmentEditDraftValues): Partial<AppointmentEditFormValues> => ({
    ...values,
    startDate: dayjs(values.startDate),
  }),
};

export function useSchedulePageController() {
  const { searchParams, setUrlState } = useUrlState();
  const requestedWeekStart = dayjs(searchParams.get("week") ?? "");
  const weekStart = requestedWeekStart.isValid() ? requestedWeekStart.startOf("week") : dayjs().startOf("week");
  const setWeekStart = useCallback(
    (next: Dayjs) => {
      const normalized = next.startOf("week");
      const currentWeek = dayjs().startOf("week");
      setUrlState({ week: normalized.isSame(currentWeek, "day") ? null : normalized.format("YYYY-MM-DD") });
    },
    [setUrlState],
  );
  const [isCreateRequestedOpen, setOpen] = useState(false);
  const isOpen = isCreateRequestedOpen;
  const [selectedAppointment, setSelectedAppointment] = useState<Appointment | null>(null);
  const [selectedAppointmentBaselineActivityId, setSelectedAppointmentBaselineActivityId] = useState<Ulid | null | undefined>();
  const [appointmentToEdit, setAppointmentToEdit] = useState<Appointment | null>(null);
  const [appointmentToEditBaselineActivityId, setAppointmentToEditBaselineActivityId] = useState<Ulid | null | undefined>();
  const [appointmentToDelete, setAppointmentToDelete] = useState<Appointment | null>(null);
  const [appointmentToDeleteBaselineActivityId, setAppointmentToDeleteBaselineActivityId] = useState<Ulid | null | undefined>();
  const [appointmentToReschedule, setAppointmentToReschedule] = useState<Appointment | null>(null);
  const [appointmentToRescheduleStartDate, setAppointmentToRescheduleStartDate] = useState<Dayjs | null>(null);
  const [appointmentToRescheduleBaselineActivityId, setAppointmentToRescheduleBaselineActivityId] = useState<Ulid | null | undefined>();
  const [isQuickClientCreateOpen, setQuickClientCreateOpen] = useState(false);
  const createdClientOptions = useCreatedReferenceOptions("client");
  const auth = useAuth();
  const providerFilterParam = searchParams.get("provider");
  const providerFilterId =
    providerFilterParam === "all" ? undefined : (providerFilterParam ?? (hasSuperuserAccess(auth.user) ? auth.user?.id : undefined));
  const setProviderFilterId = useCallback(
    (next?: string) => {
      setUrlState({ provider: next ?? (hasSuperuserAccess(auth.user) ? "all" : null) });
    },
    [auth.user, setUrlState],
  );
  const [vacationInitialPeriod, setVacationInitialPeriod] = useState<[Dayjs, Dayjs] | null>(null);
  const [editingVacationId, setEditingVacationId] = useState<string | null>(null);
  const [pendingCreateStartDate, setPendingCreateStartDate] = useState<Dayjs | null>(null);
  const [pendingCreateProviderId, setPendingCreateProviderId] = useState<string | undefined>();
  const [, setCreateClientLabel] = useState<string | undefined>();
  const [, setCreateServiceLabel] = useState<string | undefined>();
  const [, setCreateProviderLabel] = useState<string | undefined>();
  const [form] = Form.useForm<AppointmentFormValues>();
  const createRequestControllerRef = useRef<AbortController | null>(null);
  const [editForm] = Form.useForm<AppointmentEditFormValues>();
  const createSelectedClientId = Form.useWatch("clientId", form);
  const editSelectedClientId = Form.useWatch("clientId", editForm);
  const createRouteIntent = useOpenCreateRouteIntent();
  const paymentCreate = usePaymentCreateController();
  const queryClient = useQueryClient();
  const { message, modal } = AntdApp.useApp();
  const showErrors = (error: unknown) => {
    for (const errorMessage of getApiErrorMessages(error)) {
      void message.error(errorMessage);
    }
  };

  const range: [Dayjs, Dayjs] = [weekStart, weekStart.endOf("week")];
  const canCreateAppointments = hasAdminAccess(auth.user);
  const isSpecialistFilterLocked = Boolean(auth.user && !hasAdminAccess(auth.user));
  const effectiveProviderFilterId = isSpecialistFilterLocked ? auth.user?.id : providerFilterId;
  const canCreateVacations = Boolean(
    effectiveProviderFilterId && (hasSuperuserAccess(auth.user) || effectiveProviderFilterId === auth.user?.id),
  );
  const canManageVacations = Boolean(effectiveProviderFilterId && hasSuperuserAccess(auth.user));
  const lockedProviderId = isSpecialistFilterLocked ? auth.user?.id : undefined;
  const createPrefillClientId = createRouteIntent.prefillClientId;
  const isCreateModalOpen = canCreateAppointments && (isOpen || createRouteIntent.hasOpenCreateIntent);
  const createDraft = useDurableForm({
    key: APPOINTMENT_CREATE_DRAFT_KEY,
    schema: appointmentDraftSchema,
    form,
    codec: appointmentDraftCodec,
    enabled: isCreateModalOpen,
  });
  const editDraft = useDurableForm({
    key: appointmentToEdit ? `draft:appointments:edit:${appointmentToEdit.id}` : null,
    schema: appointmentEditDraftSchema,
    form: editForm,
    codec: appointmentEditDraftCodec,
    enabled: appointmentToEdit !== null,
    entity: appointmentToEdit ? { id: appointmentToEdit.id, baselineVersion: appointmentToEditBaselineActivityId ?? null } : undefined,
  });

  const openCreateModal = () => {
    if (!canCreateAppointments) {
      return;
    }

    setPendingCreateStartDate(null);
    setPendingCreateProviderId(lockedProviderId ?? effectiveProviderFilterId);
    setOpen(true);
  };

  const openPaymentCreateForAppointment = (appointment: Appointment) => {
    if (!canCreateAppointments) {
      return;
    }

    setSelectedAppointment(null);
    setSelectedAppointmentBaselineActivityId(undefined);
    paymentCreate.openCreateModal({
      clientId: appointment.client.id,
      serviceId: appointment.service.id,
    });
  };

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.repeat || isShortcutTarget(event.target)) {
        return;
      }

      if (matchesPlainKey(event, "arrowleft")) {
        event.preventDefault();
        setWeekStart(weekStart.subtract(1, "week"));
        return;
      }

      if (matchesPlainKey(event, "arrowright")) {
        event.preventDefault();
        setWeekStart(weekStart.add(1, "week"));
        return;
      }

      if (matchesPlainKey(event, "home")) {
        event.preventDefault();
        setWeekStart(dayjs().startOf("week"));
        return;
      }

      if (matchesPlainKey(event, "a")) {
        if (!canCreateAppointments) {
          return;
        }

        event.preventDefault();
        setPendingCreateStartDate(null);
        setPendingCreateProviderId(lockedProviderId ?? effectiveProviderFilterId);
        setOpen(true);
        return;
      }

      if (matchesPlainKey(event, "v") && canCreateVacations) {
        event.preventDefault();
        const start = dayjs().add(1, "hour").startOf("hour");
        setEditingVacationId(null);
        setVacationInitialPeriod([start, start.add(1, "hour")]);
        return;
      }

      if (matchesPlainKey(event, "m") && !isSpecialistFilterLocked && auth.user?.id) {
        event.preventDefault();
        setProviderFilterId(providerFilterId === auth.user.id ? undefined : auth.user.id);
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      window.removeEventListener("keydown", handleKeyDown);
    };
  }, [
    auth.user?.id,
    canCreateAppointments,
    canCreateVacations,
    effectiveProviderFilterId,
    isSpecialistFilterLocked,
    lockedProviderId,
    providerFilterId,
    setProviderFilterId,
    setWeekStart,
    weekStart,
  ]);

  const query = useQuery({
    queryKey: appointmentQueryKeys.appointments(range[0].toISOString(), range[1].toISOString()),
    queryFn: () => appointmentsApi.list({ timezone, startDate: range[0].toISOString(), endDate: range[1].toISOString() }),
    refetchInterval: getBackgroundRefetchInterval(
      Boolean(selectedAppointment || appointmentToEdit || appointmentToDelete || appointmentToReschedule),
    ),
  });
  const recurrenceTypesQuery = useQuery({
    queryKey: appointmentQueryKeys.recurrenceTypes,
    queryFn: () => appointmentsApi.recurrenceTypes(),
  });
  const providerAvailabilityQuery = useQuery({
    queryKey: userQueryKeys.availability(effectiveProviderFilterId),
    queryFn: () => {
      if (!effectiveProviderFilterId) {
        throw new Error("Provider is not selected.");
      }

      return usersApi.getAvailability(effectiveProviderFilterId);
    },
    enabled: Boolean(effectiveProviderFilterId),
    retry: false,
  });
  const allProvidersAvailabilityQuery = useQuery({
    queryKey: userQueryKeys.availabilities,
    queryFn: () => usersApi.listAvailabilities(),
    enabled: hasAdminAccess(auth.user) && !effectiveProviderFilterId,
    retry: false,
  });

  const closeVacationModal = () => {
    setVacationInitialPeriod(null);
    setEditingVacationId(null);
  };

  const openVacationModal = (period: [Dayjs, Dayjs]) => {
    setEditingVacationId(null);
    setVacationInitialPeriod(period);
  };

  const openVacationEditor = (vacationId: string) => {
    const vacation = providerAvailabilityQuery.data?.vacations.find((item) => item.id === vacationId);
    if (!vacation || !canManageVacations) {
      void message.error("Отпуск больше недоступен для изменения. Обновите календарь.");
      return;
    }

    setEditingVacationId(vacation.id);
    setVacationInitialPeriod([dayjs(vacation.startDate), dayjs(vacation.endDate)]);
  };

  const vacationMutation = useMutation({
    mutationFn: (values: VacationRangeFormValues) => {
      const providerId = effectiveProviderFilterId;
      if (!providerId || !canCreateVacations) {
        throw new Error("Выберите свой календарь или календарь сотрудника, доступный для управления.");
      }

      const input = {
        startDate: values.period[0].toISOString(),
        endDate: values.period[1].toISOString(),
      };
      if (editingVacationId && !hasSuperuserAccess(auth.user)) {
        throw new Error("Изменять утверждённые отпуска может только суперпользователь.");
      }
      if (!hasSuperuserAccess(auth.user)) {
        return vacationRequestsApi.create(input, false);
      }

      const availability = providerAvailabilityQuery.data;
      if (!availability || availability.userId !== providerId) {
        throw new Error("Дождитесь загрузки доступности выбранного сотрудника.");
      }

      if (editingVacationId && !availability.vacations.some((item) => item.id === editingVacationId)) {
        throw new Error("Отпуск уже был изменён или удалён. Обновите календарь.");
      }

      const vacations = editingVacationId
        ? availability.vacations.map((item) =>
            item.id === editingVacationId ? input : { startDate: item.startDate, endDate: item.endDate },
          )
        : [...availability.vacations.map((item) => ({ startDate: item.startDate, endDate: item.endDate })), input];

      return usersApi.updateAvailability(
        providerId,
        {
          workingHours: availability.workingHours,
          vacations,
          cancelConflictingAppointments: values.cancelConflictingAppointments,
        },
        { expectedActivityId: availability.lastActivity?.id },
      );
    },
    onSuccess: async () => {
      const successMessage = editingVacationId
        ? "Отпуск изменён"
        : hasSuperuserAccess(auth.user)
          ? "Отпуск добавлен"
          : "Заявка на отпуск отправлена";
      closeVacationModal();
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: appointmentQueryKeys.appointmentsAll }),
        queryClient.invalidateQueries({ queryKey: userQueryKeys.availability(effectiveProviderFilterId) }),
        queryClient.invalidateQueries({ queryKey: vacationRequestQueryKeys.all }),
      ]);
      void message.success(successMessage);
    },
  });

  const removeVacationMutation = useMutation({
    mutationFn: (vacationId: string) => {
      const providerId = effectiveProviderFilterId;
      const availability = providerAvailabilityQuery.data;
      if (!providerId || !canManageVacations || !availability || availability.userId !== providerId) {
        throw new Error("Отпуск больше недоступен для удаления. Обновите календарь.");
      }
      if (!availability.vacations.some((item) => item.id === vacationId)) {
        throw new Error("Отпуск уже был удалён.");
      }

      return usersApi.updateAvailability(
        providerId,
        {
          workingHours: availability.workingHours,
          vacations: availability.vacations
            .filter((item) => item.id !== vacationId)
            .map((item) => ({ startDate: item.startDate, endDate: item.endDate })),
        },
        { expectedActivityId: availability.lastActivity?.id },
      );
    },
    onSuccess: async () => {
      closeVacationModal();
      await queryClient.invalidateQueries({ queryKey: userQueryKeys.availability(effectiveProviderFilterId) });
      void message.success("Отпуск удалён");
    },
  });

  const confirmVacationRemoval = () => {
    if (!editingVacationId) {
      return;
    }

    const vacationId = editingVacationId;
    modal.confirm({
      title: "Удалить отпуск?",
      content: "Период снова станет доступен для записей.",
      okText: "Удалить",
      cancelText: "Отмена",
      okButtonProps: { danger: true },
      onOk: () => removeVacationMutation.mutateAsync(vacationId),
    });
  };

  const visibleHours = effectiveProviderFilterId
    ? getVisibleScheduleHours([providerAvailabilityQuery.data])
    : getVisibleScheduleHours(allProvidersAvailabilityQuery.data ?? []);

  const filteredAppointments = (query.data ?? []).filter((appointment) => {
    if (effectiveProviderFilterId && appointment.provider?.id !== effectiveProviderFilterId) {
      return false;
    }
    return true;
  });

  const createCourseEnrollmentsQuery = useQuery({
    queryKey: courseQueryKeys.enrollments.list({ clientId: createSelectedClientId }),
    queryFn: () => {
      if (!createSelectedClientId) {
        throw new Error("Client is not selected.");
      }

      return courseEnrollmentsApi.list({ clientId: createSelectedClientId });
    },
    enabled: Boolean(isCreateModalOpen && createSelectedClientId),
  });

  const editCourseEnrollmentsQuery = useQuery({
    queryKey: courseQueryKeys.enrollments.list({ clientId: editSelectedClientId }),
    queryFn: () => {
      if (!editSelectedClientId) {
        throw new Error("Client is not selected.");
      }

      return courseEnrollmentsApi.list({ clientId: editSelectedClientId });
    },
    enabled: Boolean(appointmentToEdit && editSelectedClientId),
  });

  const currentSelectedAppointment = selectedAppointment
    ? ((query.data ?? []).find((item) => item.id === selectedAppointment.id) ?? selectedAppointment)
    : null;
  const currentEditingAppointment = appointmentToEdit
    ? ((query.data ?? []).find((item) => item.id === appointmentToEdit.id) ?? appointmentToEdit)
    : null;
  const currentDeletingAppointment = appointmentToDelete
    ? ((query.data ?? []).find((item) => item.id === appointmentToDelete.id) ?? appointmentToDelete)
    : null;
  const currentReschedulingAppointment = appointmentToReschedule
    ? ((query.data ?? []).find((item) => item.id === appointmentToReschedule.id) ?? appointmentToReschedule)
    : null;
  const isSelectedAppointmentStale = currentSelectedAppointment
    ? isActivityStale(currentSelectedAppointment.lastActivity?.id, selectedAppointmentBaselineActivityId)
    : false;
  const isEditingAppointmentStale = currentEditingAppointment
    ? isActivityStale(currentEditingAppointment.lastActivity?.id, appointmentToEditBaselineActivityId)
    : false;
  const isDeletingAppointmentStale = currentDeletingAppointment
    ? isActivityStale(currentDeletingAppointment.lastActivity?.id, appointmentToDeleteBaselineActivityId)
    : false;
  const isReschedulingAppointmentStale = currentReschedulingAppointment
    ? isActivityStale(currentReschedulingAppointment.lastActivity?.id, appointmentToRescheduleBaselineActivityId)
    : false;

  const syncAppointmentBaseline = (appointmentId: Ulid) => {
    const freshAppointment = findItemInQueryData(
      queryClient,
      appointmentQueryKeys.appointmentsAll,
      (data) => data as Appointment[] | undefined,
      appointmentId,
    );
    if (!freshAppointment) {
      return;
    }

    if (selectedAppointment?.id === appointmentId) {
      setSelectedAppointment(freshAppointment);
      setSelectedAppointmentBaselineActivityId(freshAppointment.lastActivity?.id ?? null);
    }

    if (appointmentToEdit?.id === appointmentId) {
      setAppointmentToEdit(freshAppointment);
      setAppointmentToEditBaselineActivityId(freshAppointment.lastActivity?.id ?? null);
    }

    if (appointmentToDelete?.id === appointmentId) {
      setAppointmentToDelete(freshAppointment);
      setAppointmentToDeleteBaselineActivityId(freshAppointment.lastActivity?.id ?? null);
    }

    if (appointmentToReschedule?.id === appointmentId) {
      setAppointmentToReschedule(freshAppointment);
      setAppointmentToRescheduleBaselineActivityId(freshAppointment.lastActivity?.id ?? null);
    }
  };

  const createMutation = useMutation({
    mutationFn: (values: AppointmentFormValues) => {
      const requestController = new AbortController();
      createRequestControllerRef.current = requestController;
      return appointmentsApi
        .create(buildCreateAppointmentPayload(values, recurrenceTypesQuery.data ?? [], timezone), {
          idempotencyKey: createIdempotencyKey(),
          signal: requestController.signal,
        })
        .finally(() => {
          if (createRequestControllerRef.current === requestController) {
            createRequestControllerRef.current = null;
          }
        });
    },
    onSuccess: async () => {
      message.success("Запись создана");
      await createDraft.clearAfterSuccess();
      closeCreateModal();
      await queryClient.invalidateQueries({ queryKey: appointmentQueryKeys.appointmentsAll });
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, input, expectedActivityId }: { id: string; input: { status?: AppointmentStatus }; expectedActivityId?: Ulid }) =>
      appointmentsApi.update(id, { ...input, expectedActivityId }),
    onMutate: ({ id, input }) => {
      const nextState = (appointment: Appointment) =>
        appointment.id === id
          ? {
              ...appointment,
              ...(input.status !== undefined ? { status: input.status } : {}),
            }
          : appointment;

      setSelectedAppointment((current) => (current ? nextState(current) : current));
      queryClient.setQueriesData<Appointment[]>({ queryKey: appointmentQueryKeys.appointmentsAll }, (current) => {
        return current ? current.map(nextState) : current;
      });
    },
    onSuccess: async (_, variables) => {
      await queryClient.invalidateQueries({ queryKey: appointmentQueryKeys.appointmentsAll });
      syncAppointmentBaseline(variables.id);
    },
    meta: { suppressErrorNotification: true },
    onError: async (error, variables) => {
      await handleStaleEntityConflict({
        error,
        modal,
        queryClient,
        invalidateQueryKey: appointmentQueryKeys.appointmentsAll,
        showErrors,
        title: "Запись уже изменена",
        okText: "Повторить поверх новой версии",
        cancelText: "Обновить данные",
        onConfirm: (conflict) => {
          updateMutation.mutate({ ...variables, expectedActivityId: conflict.currentActivity?.id });
        },
        onReload: () => {
          const freshAppointment = findItemInQueryData(
            queryClient,
            appointmentQueryKeys.appointmentsAll,
            (data) => data as Appointment[] | undefined,
            variables.id,
          );
          if (!freshAppointment) {
            return;
          }

          setSelectedAppointment(freshAppointment);
          setSelectedAppointmentBaselineActivityId(freshAppointment.lastActivity?.id ?? null);
        },
      });
    },
  });

  const editMutation = useMutation({
    mutationFn: ({ id, input, expectedActivityId }: { id: string; input: AppointmentEditFormValues; expectedActivityId?: Ulid }) => {
      return appointmentsApi.update(id, {
        clientId: input.clientId,
        serviceId: input.serviceId,
        providerId: input.providerId,
        courseThemeId: input.courseThemeId ?? null,
        hasCourseThemeSelection: true,
        lessonNotes: input.lessonNotes?.trim() || null,
        hasLessonNotes: true,
        startDate: input.startDate.toISOString(),
        timezone,
        expectedActivityId,
      });
    },
    onSuccess: async () => {
      message.success("Запись обновлена");
      await editDraft.clearAfterSuccess();
      setAppointmentToEdit(null);
      setAppointmentToEditBaselineActivityId(undefined);
      await queryClient.invalidateQueries({ queryKey: appointmentQueryKeys.appointmentsAll });
    },
    meta: { suppressErrorNotification: true },
    onError: async (error, variables) => {
      if (!appointmentToEdit) {
        showErrors(error);
        return;
      }

      await handleStaleEntityConflict({
        error,
        modal,
        queryClient,
        invalidateQueryKey: appointmentQueryKeys.appointmentsAll,
        showErrors,
        title: "Запись уже изменена",
        okText: "Перезаписать",
        cancelText: "Обновить форму",
        onConfirm: (nextConflict) => {
          editMutation.mutate({ ...variables, expectedActivityId: nextConflict.currentActivity?.id });
        },
        onReload: () => {
          const freshAppointment =
            findItemInQueryData(
              queryClient,
              appointmentQueryKeys.appointmentsAll,
              (data) => data as Appointment[] | undefined,
              appointmentToEdit.id,
            ) ?? currentEditingAppointment;
          if (!freshAppointment) {
            return;
          }

          setAppointmentToEdit(freshAppointment);
          setAppointmentToEditBaselineActivityId(freshAppointment.lastActivity?.id ?? null);
          editForm.setFieldsValue({
            clientId: freshAppointment.client.id,
            serviceId: freshAppointment.service.id,
            providerId: lockedProviderId ?? freshAppointment.provider?.id,
            courseThemeId: freshAppointment.courseTheme?.id,
            lessonNotes: freshAppointment.lessonNotes ?? undefined,
            startDate: dayjs(freshAppointment.startDate),
          });
        },
      });
    },
  });

  const rescheduleMutation = useMutation({
    mutationFn: ({
      appointment,
      startDate,
      scope,
      expectedActivityId,
    }: {
      appointment: Appointment;
      startDate: Dayjs;
      scope?: AppointmentMutationScope;
      expectedActivityId?: Ulid;
    }) => {
      return appointmentsApi.update(appointment.id, {
        startDate: startDate.toISOString(),
        timezone,
        scope,
        expectedActivityId,
      });
    },
    onSuccess: async (_, variables) => {
      message.success("Запись перенесена");

      if (selectedAppointment?.id === variables.appointment.id) {
        setSelectedAppointment(null);
        setSelectedAppointmentBaselineActivityId(undefined);
      }

      if (appointmentToEdit?.id === variables.appointment.id) {
        setAppointmentToEdit(null);
        setAppointmentToEditBaselineActivityId(undefined);
      }

      if (appointmentToDelete?.id === variables.appointment.id) {
        setAppointmentToDelete(null);
        setAppointmentToDeleteBaselineActivityId(undefined);
      }

      if (appointmentToReschedule?.id === variables.appointment.id) {
        setAppointmentToReschedule(null);
        setAppointmentToRescheduleStartDate(null);
        setAppointmentToRescheduleBaselineActivityId(undefined);
      }

      await queryClient.invalidateQueries({ queryKey: appointmentQueryKeys.appointmentsAll });
      syncAppointmentBaseline(variables.appointment.id);
    },
    meta: { suppressErrorNotification: true },
    onError: async (error, variables) => {
      await handleStaleEntityConflict({
        error,
        modal,
        queryClient,
        invalidateQueryKey: appointmentQueryKeys.appointmentsAll,
        showErrors,
        title: "Запись уже изменена",
        okText: "Перенести поверх новой версии",
        cancelText: "Обновить данные",
        onConfirm: (conflict) => {
          rescheduleMutation.mutate({ ...variables, expectedActivityId: conflict.currentActivity?.id });
        },
        onReload: () => {
          const freshAppointment = findItemInQueryData(
            queryClient,
            appointmentQueryKeys.appointmentsAll,
            (data) => data as Appointment[] | undefined,
            variables.appointment.id,
          );
          if (!freshAppointment) {
            return;
          }

          if (selectedAppointment?.id === variables.appointment.id) {
            setSelectedAppointment(freshAppointment);
            setSelectedAppointmentBaselineActivityId(freshAppointment.lastActivity?.id ?? null);
          }

          if (appointmentToEdit?.id === variables.appointment.id) {
            setAppointmentToEdit(freshAppointment);
            setAppointmentToEditBaselineActivityId(freshAppointment.lastActivity?.id ?? null);
          }

          if (appointmentToDelete?.id === variables.appointment.id) {
            setAppointmentToDelete(freshAppointment);
            setAppointmentToDeleteBaselineActivityId(freshAppointment.lastActivity?.id ?? null);
          }

          if (appointmentToReschedule?.id === variables.appointment.id) {
            setAppointmentToReschedule(freshAppointment);
            setAppointmentToRescheduleStartDate(variables.startDate);
            setAppointmentToRescheduleBaselineActivityId(freshAppointment.lastActivity?.id ?? null);
          }
        },
      });
    },
  });

  const deleteMutation = useMutation({
    mutationFn: ({ id, scope, expectedActivityId }: { id: string; scope?: AppointmentMutationScope; expectedActivityId?: Ulid }) => {
      return appointmentsApi.remove(id, { scope, expectedActivityId });
    },
    onSuccess: async () => {
      message.success("Запись удалена");
      setSelectedAppointment(null);
      setSelectedAppointmentBaselineActivityId(undefined);
      setAppointmentToDelete(null);
      setAppointmentToDeleteBaselineActivityId(undefined);
      await queryClient.invalidateQueries({ queryKey: appointmentQueryKeys.appointmentsAll });
    },
    meta: { suppressErrorNotification: true },
    onError: async (error, variables) => {
      await handleStaleEntityConflict({
        error,
        modal,
        queryClient,
        invalidateQueryKey: appointmentQueryKeys.appointmentsAll,
        showErrors,
        title: "Запись уже изменена",
        okText: "Удалить все равно",
        cancelText: "Обновить данные",
        onConfirm: (conflict) => {
          deleteMutation.mutate({ ...variables, expectedActivityId: conflict.currentActivity?.id });
        },
        onReload: () => {
          const freshAppointment = findItemInQueryData(
            queryClient,
            appointmentQueryKeys.appointmentsAll,
            (data) => data as Appointment[] | undefined,
            variables.id,
          );
          if (!freshAppointment) {
            return;
          }

          if (appointmentToDelete?.id === variables.id) {
            setAppointmentToDelete(freshAppointment);
            setAppointmentToDeleteBaselineActivityId(freshAppointment.lastActivity?.id ?? null);
          }

          setSelectedAppointment(freshAppointment);
          setSelectedAppointmentBaselineActivityId(freshAppointment.lastActivity?.id ?? null);
        },
      });
    },
  });

  const handleCreateDraftChange = (_values: AppointmentFormValues) => {
    createDraft.formProps.onValuesChange?.({}, _values);
  };

  const openCreateModalAt = (startDate: Dayjs) => {
    if (!canCreateAppointments) {
      return;
    }

    setPendingCreateStartDate(startDate.second(0).millisecond(0));
    setPendingCreateProviderId(lockedProviderId ?? effectiveProviderFilterId);
    setOpen(true);
  };

  useEffect(() => {
    if (!isCreateModalOpen) {
      return;
    }

    form.setFieldsValue({
      clientId: createPrefillClientId,
      serviceId: undefined,
      providerId: pendingCreateProviderId ?? lockedProviderId,
      courseThemeId: undefined,
      lessonNotes: undefined,
      startDate: pendingCreateStartDate ?? dayjs(),
      recurrenceTypeId: undefined,
      patternEndDate: undefined,
      weeklyDays: undefined,
    });
  }, [createPrefillClientId, form, isCreateModalOpen, lockedProviderId, pendingCreateProviderId, pendingCreateStartDate]);

  function closeCreateModal() {
    createRequestControllerRef.current?.abort();
    createRequestControllerRef.current = null;
    setOpen(false);
    setPendingCreateStartDate(null);
    setPendingCreateProviderId(undefined);
    form.resetFields();
    createRouteIntent.clearOpenCreateIntent();
  }

  function handleClearCreateDraft() {
    void createDraft.discard().then(() => {
      form.setFieldsValue({
        clientId: createPrefillClientId,
        serviceId: undefined,
        providerId: pendingCreateProviderId ?? lockedProviderId,
        courseThemeId: undefined,
        lessonNotes: undefined,
        startDate: pendingCreateStartDate ?? dayjs(),
        recurrenceTypeId: undefined,
        patternEndDate: undefined,
        weeklyDays: undefined,
      });
    });
  }

  return {
    auth,
    canCreateAppointments,
    canCreateVacations,
    canManageVacations,
    weekStart,
    setWeekStart,
    query,
    recurrenceTypesQuery,
    createCourseEnrollmentsQuery,
    editCourseEnrollmentsQuery,
    providerAvailabilityQuery,
    allProvidersAvailabilityQuery,
    visibleHours,
    filteredAppointments,
    currentSelectedAppointment,
    currentEditingAppointment,
    currentDeletingAppointment,
    currentReschedulingAppointment,
    appointmentToRescheduleStartDate,
    isSelectedAppointmentStale,
    isEditingAppointmentStale: isEditingAppointmentStale || editDraft.isStale,
    isDeletingAppointmentStale,
    isReschedulingAppointmentStale,
    selectedAppointment,
    selectedAppointmentBaselineActivityId,
    appointmentToEdit,
    appointmentToEditBaselineActivityId,
    appointmentToDeleteBaselineActivityId,
    isQuickClientCreateOpen,
    setQuickClientCreateOpen,
    createdClientOptions: createdClientOptions.createdOptions,
    providerFilterId,
    setProviderFilterId,
    effectiveProviderFilterId,
    isSpecialistFilterLocked,
    lockedProviderId,
    form,
    editForm,
    hasCreateDraft: createDraft.hasDraft,
    isCreateDraftRestored: createDraft.restored,
    createDraftSaveStatus: createDraft.status,
    createDraftRetry: createDraft.retry,
    isCreateModalOpen,
    createMutation,
    updateMutation,
    editMutation,
    rescheduleMutation,
    deleteMutation,
    vacationMutation,
    removeVacationMutation,
    vacationInitialPeriod,
    editingVacationId,
    openVacationModal,
    openVacationEditor,
    closeVacationModal,
    confirmVacationRemoval,
    openCreateModal,
    openCreateModalAt,
    closeCreateModal,
    handleCreateDraftChange,
    onEditDraftChange: editDraft.formProps.onValuesChange,
    editDraft,
    handleClearCreateDraft,
    setSelectedAppointment,
    setSelectedAppointmentBaselineActivityId,
    setAppointmentToEdit,
    setAppointmentToEditBaselineActivityId,
    setAppointmentToDelete,
    setAppointmentToDeleteBaselineActivityId,
    setAppointmentToReschedule,
    setAppointmentToRescheduleStartDate,
    setAppointmentToRescheduleBaselineActivityId,
    setCreateClientLabel,
    setCreateServiceLabel,
    setCreateProviderLabel,
    openPaymentCreateForAppointment,
    paymentCreate,
    createPrefillClientId,
    onQuickClientCreated: (client: { id: string; displayName: string }) => {
      createdClientOptions.addCreatedOption({
        id: client.id,
        label: client.displayName,
      });
      setCreateClientLabel(client.displayName);

      if (isCreateModalOpen) {
        form.setFieldValue("clientId", client.id);
      }

      if (appointmentToEdit) {
        editForm.setFieldValue("clientId", client.id);
      }

      setQuickClientCreateOpen(false);
    },
    createCourseThemeOptions: buildCourseThemeOptions(createCourseEnrollmentsQuery.data ?? []),
    editCourseThemeOptions: buildCourseThemeOptions(editCourseEnrollmentsQuery.data ?? []),
    modal,
  };
}

function buildCreateAppointmentPayload(values: AppointmentFormValues, recurrenceTypes: RecurrenceType[], timezone: string) {
  const recurrenceType = recurrenceTypes.find((item) => item.id === values.recurrenceTypeId);
  if (!values.startDate) {
    throw new Error("Укажите дату и время начала записи.");
  }

  return {
    clientId: values.clientId,
    serviceId: values.serviceId,
    providerId: values.providerId,
    courseThemeId: values.courseThemeId,
    lessonNotes: values.lessonNotes?.trim() || undefined,
    startDate: values.startDate.toISOString(),
    timezone,
    recurrenceTypeId: recurrenceType?.id,
    patternEndDate: recurrenceType ? values.patternEndDate?.endOf("day").toISOString() : undefined,
    recurrencePattern: recurrenceType ? getRecurrencePattern(recurrenceType.key, values.startDate, values.weeklyDays) : undefined,
  };
}

function getRecurrencePattern(key: RecurrenceType["key"], startDate: Dayjs, weeklyDays?: number[]) {
  if (key === "daily") {
    return 1;
  }

  if (key === "weekly") {
    return (weeklyDays ?? []).reduce((sum, day) => sum + day, 0);
  }

  return startDate.date();
}

function serializeAppointmentDraft(values: AppointmentFormValues): AppointmentDraftValues {
  return {
    clientId: values.clientId,
    serviceId: values.serviceId,
    providerId: values.providerId,
    courseThemeId: values.courseThemeId,
    lessonNotes: values.lessonNotes,
    startDate: values.startDate?.toISOString(),
    recurrenceTypeId: values.recurrenceTypeId,
    patternEndDate: values.patternEndDate?.toISOString(),
    weeklyDays: values.weeklyDays,
  };
}

function buildCourseThemeOptions(enrollments: CourseEnrollment[]): DefaultOptionType[] {
  return enrollments.map((enrollment) => ({
    label: enrollment.courseName,
    options: enrollment.themes.map((theme) => ({
      value: theme.courseThemeId,
      label: `${theme.themeTitle} · ${getCourseThemeProgressStateLabel(theme.state)}`,
    })),
  }));
}

function getCourseThemeProgressStateLabel(state: CourseThemeProgressState) {
  switch (state) {
    case 0:
      return "заблокировано";
    case 1:
      return "можно открыть";
    case 2:
      return "открыто";
    case 3:
      return "в процессе";
    case 4:
      return "ждет ДЗ";
    case 5:
      return "завершено";
  }
}
