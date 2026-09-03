import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { App as AntdApp, Form } from "antd";
import dayjs, { type Dayjs } from "dayjs";
import { useEffect, useState } from "react";
import * as v from "valibot";

import { normalizePhone, normalizeSocialLink } from "@/entities/client";
import {
  authApi,
  authQueryKeys,
  type ChangePasswordInput,
  hasSuperuserAccess,
  type RecoveryCodeItem,
  type SessionDto,
  type Setup2FaInput,
  type Setup2FaResponse,
  useAuth,
} from "@/entities/session";
import {
  calendarSubscriptionsApi,
  type UserAvailability,
  userQueryKeys,
  usersApi,
  type WeekdayKey,
  workingHoursRequestQueryKeys,
  workingHoursRequestsApi,
} from "@/entities/user";
import { weekdayOrder } from "@/entities/user";
import { onboardingApi, onboardingQueryKeys } from "@/features/onboarding";
import type { Ulid } from "@/shared/api";
import { getApiErrorMessages } from "@/shared/api";
import { isShortcutTarget, matchesPlainKey } from "@/shared/lib";
import { handleStaleEntityConflict } from "@/shared/lib";
import { jsonDurableFormCodec, useDurableForm } from "@/shared/lib/react";
import { useUrlCopyModal } from "@/shared/ui";

type TotpSetupState = Setup2FaResponse & { password: string };

export type AvailabilityFormValues = {
  workingHours: Array<{
    dayOfWeek: WeekdayKey;
    isWorkingDay: boolean;
    timeRange?: [Dayjs, Dayjs];
  }>;
  vacations: Array<{
    period: [Dayjs, Dayjs];
  }>;
};

type SaveAvailabilityInput = {
  values: AvailabilityFormValues;
  expectedActivityId?: string;
};

export type PersonalInfoFormValues = {
  firstName: string;
  lastName: string;
  phone?: string | null;
  telegram?: string | null;
  vk?: string | null;
};

type AvailabilityDraftValues = {
  workingHours: Array<{ dayOfWeek: WeekdayKey; isWorkingDay: boolean; timeRange?: [string, string] }>;
  vacations: Array<{ period: [string, string] }>;
};

const personalInfoDraftSchema = v.object({
  firstName: v.string(),
  lastName: v.string(),
  phone: v.optional(v.nullable(v.string())),
  telegram: v.optional(v.nullable(v.string())),
  vk: v.optional(v.nullable(v.string())),
});
const availabilityDraftSchema = v.object({
  workingHours: v.array(
    v.object({
      dayOfWeek: v.picklist(weekdayOrder),
      isWorkingDay: v.boolean(),
      timeRange: v.optional(v.tuple([v.string(), v.string()])),
    }),
  ),
  vacations: v.array(v.object({ period: v.tuple([v.string(), v.string()]) })),
});
const personalInfoDraftCodec = jsonDurableFormCodec<PersonalInfoFormValues>();
const availabilityDraftCodec = {
  serialize: (values: AvailabilityFormValues): AvailabilityDraftValues => ({
    workingHours: values.workingHours.map((item) => ({
      ...item,
      timeRange: item.timeRange ? [item.timeRange[0].toISOString(), item.timeRange[1].toISOString()] : undefined,
    })),
    vacations: values.vacations
      .filter((item) => hasVacationPeriod(item.period))
      .map((item) => ({
        period: [item.period[0].toISOString(), item.period[1].toISOString()],
      })),
  }),
  deserialize: (values: AvailabilityDraftValues): Partial<AvailabilityFormValues> => ({
    workingHours: values.workingHours.map((item) => ({
      ...item,
      timeRange: item.timeRange ? [dayjs(item.timeRange[0]), dayjs(item.timeRange[1])] : undefined,
    })),
    vacations: values.vacations.map((item) => ({ period: [dayjs(item.period[0]), dayjs(item.period[1])] })),
  }),
};

type SavePersonalInfoInput = {
  values: PersonalInfoFormValues;
  expectedActivityId?: Ulid;
};

function hasVacationPeriod(period?: [Dayjs, Dayjs]) {
  return Boolean(period?.[0] && period[1]);
}

function mapAvailabilityToForm(availability: UserAvailability): AvailabilityFormValues {
  return {
    workingHours: weekdayOrder.map((dayOfWeek) => {
      const item = availability.workingHours.find((entry) => entry.dayOfWeek === dayOfWeek);
      return {
        dayOfWeek,
        isWorkingDay: item?.isWorkingDay ?? false,
        timeRange:
          item?.isWorkingDay && item.startTime && item.endTime ? [timeToDayjs(item.startTime), timeToDayjs(item.endTime)] : undefined,
      };
    }),
    vacations: availability.vacations.map((vacation) => ({
      period: [dayjs(vacation.startDate), dayjs(vacation.endDate)],
    })),
  };
}

function timeToDayjs(value: string) {
  const [hours = "0", minutes = "0"] = value.split(":");
  return dayjs().hour(Number(hours)).minute(Number(minutes)).second(0).millisecond(0);
}

export function useProfilePageController() {
  const auth = useAuth();
  const requiresScheduleApproval = !hasSuperuserAccess(auth.user);
  const urlModal = useUrlCopyModal(auth.user?.id);
  const { message, modal } = AntdApp.useApp();
  const queryClient = useQueryClient();
  const [setupState, setSetupState] = useState<TotpSetupState | null>(null);
  const [recoveryCodes, setRecoveryCodes] = useState<RecoveryCodeItem[] | null>(null);
  const [personalInfoForm] = Form.useForm<PersonalInfoFormValues>();
  const [availabilityForm] = Form.useForm<AvailabilityFormValues>();
  const showErrors = (error: unknown) => {
    for (const errorMessage of getApiErrorMessages(error)) {
      void message.error(errorMessage);
    }
  };

  const meQuery = useQuery({
    queryKey: authQueryKeys.me,
    queryFn: () => authApi.getMe(),
    gcTime: 0,
  });

  const sessionsQuery = useQuery({
    queryKey: authQueryKeys.sessions,
    queryFn: () => authApi.getSessions(),
    gcTime: 0,
    staleTime: 0,
  });

  const availabilityQuery = useQuery({
    queryKey: userQueryKeys.availability(meQuery.data?.id),
    queryFn: () => {
      const userId = meQuery.data?.id;
      if (!userId) {
        throw new Error("User id is missing.");
      }

      return usersApi.getAvailability(userId);
    },
    enabled: Boolean(meQuery.data?.id),
  });
  const personalInfoDraft = useDurableForm({
    key: meQuery.data ? `draft:profile:personal:${meQuery.data.id}` : null,
    schema: personalInfoDraftSchema,
    form: personalInfoForm,
    codec: personalInfoDraftCodec,
    enabled: Boolean(meQuery.data),
    entity: meQuery.data ? { id: meQuery.data.id, baselineVersion: meQuery.data.lastActivity?.id ?? null } : undefined,
  });
  const availabilityDraft = useDurableForm({
    key: availabilityQuery.data ? `draft:profile:availability:${availabilityQuery.data.userId}` : null,
    schema: availabilityDraftSchema,
    form: availabilityForm,
    codec: availabilityDraftCodec,
    enabled: Boolean(availabilityQuery.data),
    entity: availabilityQuery.data
      ? { id: availabilityQuery.data.userId, baselineVersion: availabilityQuery.data.lastActivity?.id ?? null }
      : undefined,
  });

  const changePasswordMutation = useMutation({
    mutationFn: (input: ChangePasswordInput) => authApi.changePassword(input),
    onSuccess: async () => {
      message.success("Пароль изменен. Войдите снова.");
      await auth.logout();
    },
    onError: showErrors,
  });

  const setup2FaMutation = useMutation({
    mutationFn: (input: Setup2FaInput) => authApi.setup2Fa(input),
    onSuccess: (data, variables) => {
      setSetupState({ ...data, password: variables.password });
    },
    onError: showErrors,
  });

  const verify2FaMutation = useMutation({
    mutationFn: ({ otp }: { otp: string }) => {
      const me = meQuery.data;
      if (!setupState || !me) {
        throw new Error("Нет данных для настройки 2FA");
      }

      return authApi.verify2Fa({
        email: me.email,
        otp,
        otpSecret: setupState.secret,
      });
    },
    onSuccess: (data) => {
      message.success("2FA включен. Сохраните коды восстановления.");
      setSetupState(null);
      setRecoveryCodes(data.allCodes);
      void meQuery.refetch();
    },
    onError: showErrors,
  });

  const getRecoveryCodesMutation = useMutation({
    mutationFn: () => authApi.getRecoveryCodes(),
    onSuccess: (data) => {
      setRecoveryCodes(data.allCodes);
    },
    onError: showErrors,
  });

  const regenerateRecoveryCodesMutation = useMutation({
    mutationFn: () => authApi.regenerateRecoveryCodes(),
    onSuccess: (data) => {
      message.success("Новые коды восстановления созданы.");
      setRecoveryCodes(data.allCodes);
    },
    onError: showErrors,
  });

  const remove2FaMutation = useMutation({
    mutationFn: () => authApi.remove2Fa(),
    onSuccess: () => {
      message.success("2FA отключен.");
      void meQuery.refetch();
      setRecoveryCodes(null);
    },
    onError: showErrors,
  });
  const getRecoveryCodes = getRecoveryCodesMutation.mutate;
  const regenerateRecoveryCodes = regenerateRecoveryCodesMutation.mutate;
  const removeTwoFactorAuthentication = remove2FaMutation.mutate;

  const logoutAllMutation = useMutation({
    mutationFn: () => authApi.logoutAll(),
    onSuccess: async () => {
      message.success("Все сессии завершены. Войдите снова.");
      await auth.logout();
    },
    onError: showErrors,
  });

  const revokeSessionMutation = useMutation({
    mutationFn: async (session: SessionDto) => {
      await authApi.revokeSession(session.id);
      return session;
    },
    onSuccess: async (session) => {
      message.success(session.isCurrent ? "Текущая сессия завершена." : "Сессия завершена.");

      if (session.isCurrent) {
        await auth.logout();
        return;
      }

      await queryClient.invalidateQueries({
        queryKey: authQueryKeys.sessions,
      });
    },
    onError: showErrors,
  });

  const savePersonalInfoMutation = useMutation({
    mutationFn: ({ values, expectedActivityId }: SavePersonalInfoInput) => {
      const userId = meQuery.data?.id;
      if (!userId) {
        throw new Error("User id is missing.");
      }

      return usersApi.update(
        userId,
        {
          firstName: values.firstName.trim(),
          lastName: values.lastName.trim(),
          phone: normalizePhone(values.phone),
          telegram: normalizeSocialLink(values.telegram, "telegram"),
          vk: normalizeSocialLink(values.vk, "vk"),
        },
        { expectedActivityId },
      );
    },
    onSuccess: async () => {
      message.success("Данные профиля сохранены");
      await personalInfoDraft.clearAfterSuccess();
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: authQueryKeys.me }),
        queryClient.invalidateQueries({ queryKey: userQueryKeys.all }),
      ]);
    },
    onError: async (error, variables) => {
      await handleStaleEntityConflict({
        error,
        modal,
        queryClient,
        invalidateQueryKey: authQueryKeys.me,
        showErrors,
        title: "Профиль уже изменен",
        okText: "Сохранить все равно",
        cancelText: "Обновить данные",
        onConfirm: (conflict) => {
          savePersonalInfoMutation.mutate({
            values: variables.values,
            expectedActivityId: conflict.currentActivity?.id,
          });
        },
        onReload: () => {
          void Promise.all([
            queryClient.invalidateQueries({ queryKey: authQueryKeys.me }),
            queryClient.invalidateQueries({ queryKey: userQueryKeys.all }),
          ]);
        },
      });
    },
  });

  const saveAvailabilityMutation = useMutation({
    mutationFn: ({ values, expectedActivityId }: SaveAvailabilityInput) => {
      const workingHours = values.workingHours.map((item) => ({
        dayOfWeek: item.dayOfWeek,
        isWorkingDay: item.isWorkingDay,
        startTime: item.isWorkingDay && item.timeRange?.[0] ? item.timeRange[0].format("HH:mm") : null,
        endTime: item.isWorkingDay && item.timeRange?.[1] ? item.timeRange[1].format("HH:mm") : null,
      }));
      if (requiresScheduleApproval) {
        return workingHoursRequestsApi.create({ workingHours });
      }

      const userId = meQuery.data?.id;
      if (!userId) {
        throw new Error("User id is missing.");
      }
      return usersApi.updateAvailability(
        userId,
        {
          workingHours,
          vacations: (availabilityQuery.data?.vacations ?? []).map((item) => ({
            startDate: item.startDate,
            endDate: item.endDate,
          })),
        },
        { expectedActivityId },
      );
    },
    onSuccess: async () => {
      message.success(requiresScheduleApproval ? "Заявка на изменение рабочих дней отправлена" : "График работы сохранен");
      await availabilityDraft.clearAfterSuccess();
      await queryClient.invalidateQueries({
        queryKey: requiresScheduleApproval ? workingHoursRequestQueryKeys.all : userQueryKeys.availability(meQuery.data?.id),
      });
    },
    onError: async (error, variables) => {
      if (requiresScheduleApproval) {
        showErrors(error);
        return;
      }
      await handleStaleEntityConflict({
        error,
        modal,
        queryClient,
        invalidateQueryKey: userQueryKeys.availability(meQuery.data?.id),
        showErrors,
        title: "График уже изменен",
        okText: "Сохранить все равно",
        cancelText: "Обновить данные",
        onConfirm: (conflict) => {
          saveAvailabilityMutation.mutate({
            values: variables.values,
            expectedActivityId: conflict.currentActivity?.id,
          });
        },
        onReload: () => {
          void queryClient.invalidateQueries({ queryKey: userQueryKeys.availability(meQuery.data?.id) });
        },
      });
    },
  });

  const resetOnboardingMutation = useMutation({
    mutationFn: () => onboardingApi.reset(),
    onSuccess: async () => {
      message.success("Экскурсия начнётся сначала.");
      await queryClient.invalidateQueries({
        queryKey: onboardingQueryKeys.state,
      });
    },
    onError: showErrors,
  });

  const calendarSubscriptionMutation = useMutation({
    mutationFn: (userId: Ulid) => calendarSubscriptionsApi.regenerateUser(userId),
    onSuccess: (subscription) => {
      urlModal.openUrlModal({
        url: subscription.url,
        title: "Подписка на календарь",
        description: "Скопируйте ссылку и добавьте её в приложение календаря.",
        warning: "Предыдущая ссылка на календарь уже отключена.",
      });
      message.success("Ссылка на календарь создана");
    },
    onError: showErrors,
  });

  const me = meQuery.data;
  const isTwoFactorEnabled = me?.isTwoFactorEnabled ?? false;
  const isTwoFactorRequired = me?.isTwoFactorRequired ?? false;

  useEffect(() => {
    if (!meQuery.data) {
      return;
    }

    personalInfoForm.setFieldsValue({
      firstName: meQuery.data.firstName,
      lastName: meQuery.data.lastName,
      phone: meQuery.data.phone ?? undefined,
      telegram: meQuery.data.telegram ?? undefined,
      vk: meQuery.data.vk ?? undefined,
    });
  }, [meQuery.data, personalInfoForm]);

  useEffect(() => {
    if (!availabilityQuery.data) {
      return;
    }

    availabilityForm.setFieldsValue(mapAvailabilityToForm(availabilityQuery.data));
  }, [availabilityForm, availabilityQuery.data]);

  useEffect(() => {
    return () => {
      queryClient.removeQueries({ queryKey: authQueryKeys.sessions });
    };
  }, [queryClient]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.repeat || isShortcutTarget(event.target) || !isTwoFactorEnabled) {
        return;
      }

      if (matchesPlainKey(event, "r")) {
        event.preventDefault();
        getRecoveryCodes();
        return;
      }

      if (matchesPlainKey(event, "g")) {
        event.preventDefault();
        regenerateRecoveryCodes();
        return;
      }

      if (matchesPlainKey(event, "o") && !isTwoFactorRequired) {
        event.preventDefault();
        removeTwoFactorAuthentication();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      window.removeEventListener("keydown", handleKeyDown);
    };
  }, [getRecoveryCodes, isTwoFactorEnabled, isTwoFactorRequired, regenerateRecoveryCodes, removeTwoFactorAuthentication]);

  return {
    me,
    setupState,
    recoveryCodes,
    personalInfoForm,
    availabilityForm,
    personalInfoDraft,
    availabilityDraft,
    discardPersonalInfoDraft: async () => {
      await personalInfoDraft.discard();
      if (meQuery.data) {
        personalInfoForm.setFieldsValue({
          firstName: meQuery.data.firstName,
          lastName: meQuery.data.lastName,
          phone: meQuery.data.phone ?? undefined,
          telegram: meQuery.data.telegram ?? undefined,
          vk: meQuery.data.vk ?? undefined,
        });
      }
    },
    discardAvailabilityDraft: async () => {
      await availabilityDraft.discard();
      if (availabilityQuery.data) {
        availabilityForm.setFieldsValue(mapAvailabilityToForm(availabilityQuery.data));
      }
    },
    meQuery,
    sessionsQuery,
    availabilityQuery,
    changePasswordMutation,
    setup2FaMutation,
    verify2FaMutation,
    getRecoveryCodesMutation,
    regenerateRecoveryCodesMutation,
    remove2FaMutation,
    logoutAllMutation,
    revokeSessionMutation,
    savePersonalInfoMutation,
    saveAvailabilityMutation,
    resetOnboardingMutation,
    calendarSubscriptionMutation,
    urlModalProps: urlModal.urlModalProps,
    isTwoFactorEnabled,
    isTwoFactorRequired,
    requiresScheduleApproval,
    onPersonalInfoSubmit: (values: PersonalInfoFormValues) => {
      savePersonalInfoMutation.mutate({
        values,
        expectedActivityId: meQuery.data?.lastActivity?.id,
      });
    },
    onAvailabilitySubmit: (values: AvailabilityFormValues) => {
      saveAvailabilityMutation.mutate({
        values,
        expectedActivityId: availabilityQuery.data?.lastActivity?.id,
      });
    },
    onPasswordSubmit: (values: ChangePasswordInput) => {
      changePasswordMutation.mutate(values);
    },
    onSetup2FaSubmit: (values: Setup2FaInput) => {
      setup2FaMutation.mutate(values);
    },
    onVerify2FaSubmit: (values: { otp: string }) => {
      verify2FaMutation.mutate(values);
    },
    showRecoveryCodes: () => {
      getRecoveryCodesMutation.mutate();
    },
    regenerateRecoveryCodes: () => {
      regenerateRecoveryCodesMutation.mutate();
    },
    remove2Fa: () => {
      remove2FaMutation.mutate();
    },
    logoutAll: () => {
      logoutAllMutation.mutate();
    },
    revokeSession: (session: SessionDto) => {
      revokeSessionMutation.mutate(session);
    },
    resetOnboarding: () => {
      resetOnboardingMutation.mutate();
    },
  };
}
