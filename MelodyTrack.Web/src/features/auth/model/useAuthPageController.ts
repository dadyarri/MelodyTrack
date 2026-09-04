import { useMutation, useQuery } from "@tanstack/react-query";
import { App as AntdApp, Form } from "antd";
import { useEffect, useState } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router";

import {
  authApi,
  type LoginAttemptResult,
  type LoginInput,
  type Recover2FaInput,
  type Recover2FaResponse,
  type RecoveryCodeItem,
  type RecoveryCodesResponse,
  type RegisterInput,
} from "@/entities/session";
import { authQueryKeys, useAuth } from "@/entities/session";
import { getApiErrorMessage, normalizeAppError } from "@/shared/api";

export type AuthMode = "login" | "register" | "recover2fa";
export type TotpSetup = {
  email: string;
  secret: string;
  otpUrl: string;
};
export type Recover2FaState = Recover2FaResponse & { email: string };
export type SecondFactorMode = "otp" | "recoveryCode";
export type LoginChallengeState = {
  email: string;
  password: string;
  canUseOtp: boolean;
  canUseRecoveryCode: boolean;
};

export function useAuthPageController() {
  const auth = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const hasInviteCode = searchParams.has("inviteCode");
  const inviteCode = searchParams.get("inviteCode") ?? "";
  const [mode, setMode] = useState<AuthMode>("login");
  const [totpSetup, setTotpSetup] = useState<TotpSetup | null>(null);
  const [recoveryCodes, setRecoveryCodes] = useState<RecoveryCodeItem[] | null>(null);
  const [recoveryCodeUsername, setRecoveryCodeUsername] = useState<string>("user");
  const [recover2FaState, setRecover2FaState] = useState<Recover2FaState | null>(null);
  const [loginSecondFactorMode, setLoginSecondFactorMode] = useState<SecondFactorMode>("otp");
  const [loginChallenge, setLoginChallenge] = useState<LoginChallengeState | null>(null);
  const [passwordResetGuidanceVisible, setPasswordResetGuidanceVisible] = useState(false);
  const [loginForm] = Form.useForm<Pick<LoginInput, "email" | "password">>();
  const [secondFactorForm] = Form.useForm<Pick<LoginInput, "otp" | "recoveryCode">>();
  const [registerForm] = Form.useForm<RegisterInput>();
  const [recover2FaForm] = Form.useForm<Recover2FaInput>();
  const watchedInviteCode = Form.useWatch("inviteCode", registerForm);
  const registerInviteCode = (watchedInviteCode || inviteCode).trim();
  const { message } = AntdApp.useApp();
  const inviteQuery = useQuery({
    queryKey: authQueryKeys.invite(registerInviteCode),
    queryFn: () => authApi.getInviteInfo(registerInviteCode),
    enabled: hasInviteCode && Boolean(registerInviteCode),
    retry: false,
  });

  const inviteEmail = inviteQuery.data?.email ?? "";
  const inviteErrorMessage = inviteQuery.error ? getApiErrorMessage(inviteQuery.error) : null;
  const inviteLookupFinished = !inviteQuery.isPending;
  const canSubmitRegistration = Boolean(registerInviteCode) && !inviteQuery.isError && !inviteQuery.isPending;

  useEffect(() => {
    if (!registerInviteCode && !inviteEmail) {
      return;
    }

    registerForm.setFieldsValue({
      inviteCode: registerInviteCode,
      ...(inviteEmail ? { email: inviteEmail } : {}),
    });
  }, [inviteEmail, registerForm, registerInviteCode]);

  const finishLogin = async (result: Extract<LoginAttemptResult, { kind: "success" }>) => {
    const me = await auth.establishSession(result.accessToken);
    const fallbackPath = me.isClientPortal ? "/portal" : "/";
    await navigate((location.state as { from?: { pathname?: string } } | null)?.from?.pathname ?? fallbackPath, { replace: true });
  };

  const loginMutation = useMutation({
    mutationFn: (input: Pick<LoginInput, "email" | "password">) => authApi.login(input),
    onMutate: () => {
      setPasswordResetGuidanceVisible(false);
    },
    onSuccess: async (result, credentials) => {
      if (result.kind === "challenge") {
        if (!result.canUseOtp && !result.canUseRecoveryCode) {
          message.error("Для этой учетной записи требуется второй фактор, но доступные способы не настроены.");
          return;
        }

        setLoginChallenge({
          email: credentials.email,
          password: credentials.password,
          canUseOtp: result.canUseOtp,
          canUseRecoveryCode: result.canUseRecoveryCode,
        });
        const nextMode = result.canUseOtp ? "otp" : "recoveryCode";
        setLoginSecondFactorMode(nextMode);
        secondFactorForm.resetFields();
        return;
      }

      await finishLogin(result);
    },
    onError: (error) => {
      setPasswordResetGuidanceVisible(normalizeAppError(error).status === 401);
    },
  });

  const loginSecondFactorMutation = useMutation({
    mutationFn: async (values: Pick<LoginInput, "otp" | "recoveryCode">) => {
      if (!loginChallenge) {
        throw new Error("Нет данных для проверки второго фактора");
      }

      const result = await authApi.login({
        email: loginChallenge.email,
        password: loginChallenge.password,
        otp: values.otp,
        recoveryCode: values.recoveryCode,
      });

      if (result.kind !== "success") {
        throw new Error("Не удалось подтвердить второй фактор. Попробуйте еще раз.");
      }

      return result;
    },
    onSuccess: async (result) => {
      setLoginChallenge(null);
      secondFactorForm.resetFields();
      await finishLogin(result);
    },
  });

  const registerMutation = useMutation({
    mutationFn: (input: RegisterInput) => authApi.register(input),
    onSuccess: (data, input) => {
      if (data.totpRequired) {
        if (!data.secret || !data.otpUrl) {
          message.error("Пользователь создан, но сервер не вернул данные для настройки 2FA.");
          return;
        }

        setTotpSetup({
          email: inviteEmail || input.email,
          secret: data.secret,
          otpUrl: data.otpUrl,
        });
        setRecoveryCodeUsername(inviteEmail || input.email);
        setRecoveryCodes(null);
        message.success("Пользователь создан. Завершите настройку 2FA и сохраните коды восстановления.");
        return;
      }

      message.success("Пользователь создан. Теперь можно войти.");
      setMode("login");
      void navigate("/login", { replace: true });
    },
  });

  const verify2FaMutation = useMutation({
    mutationFn: ({ otp }: { otp: string }) => {
      if (!totpSetup) {
        throw new Error("Нет данных для настройки 2FA");
      }

      return authApi.verify2Fa({
        email: totpSetup.email,
        otp,
        otpSecret: totpSetup.secret,
      });
    },
    onSuccess: (data: RecoveryCodesResponse) => {
      message.success("2FA настроена. Сохраните коды восстановления.");
      setRecoveryCodes(data.allCodes);
      setTotpSetup(null);
      setMode("login");
    },
  });

  const recover2FaMutation = useMutation({
    mutationFn: ({ email, recoveryCode }: { email: string; recoveryCode: string }) => authApi.recover2Fa({ email, recoveryCode }),
    onSuccess: (data, values) => {
      setRecover2FaState({ ...data, email: values.email });
      setRecoveryCodeUsername(values.email);
      message.success("Доступ восстановлен. Настройте новое приложение и сохраните новые коды восстановления.");
    },
  });

  return {
    auth,
    mode,
    setMode,
    totpSetup,
    recoveryCodes,
    recoveryCodeUsername,
    recover2FaState,
    loginSecondFactorMode,
    loginChallenge,
    passwordResetGuidanceVisible,
    setLoginSecondFactorMode,
    loginForm,
    secondFactorForm,
    registerForm,
    recover2FaForm,
    inviteCode,
    hasInviteCode,
    inviteQuery,
    inviteEmail,
    inviteErrorMessage,
    inviteLookupFinished,
    canSubmitRegistration,
    loginMutation,
    loginSecondFactorMutation,
    registerMutation,
    verify2FaMutation,
    recover2FaMutation,
    continueAfterRecovered2Fa: async () => {
      if (!recover2FaState) {
        return;
      }

      const me = await auth.establishSession(recover2FaState.accessToken);
      void navigate(me.isClientPortal ? "/portal" : "/", { replace: true });
    },
    continueAfterRecoveryCodes: () => {
      setRecoveryCodes(null);
      void navigate("/login", { replace: true });
    },
    onRegisterSubmit: (values: RegisterInput) => {
      registerMutation.mutate(values);
    },
    onLoginSubmit: (values: LoginInput) => {
      if (!loginMutation.isPending) {
        loginMutation.mutate(values);
      }
    },
    onLoginSecondFactorSubmit: (values: Pick<LoginInput, "otp" | "recoveryCode">) => {
      if (!loginSecondFactorMutation.isPending) {
        loginSecondFactorMutation.mutate(values);
      }
    },
    resetLoginChallenge: () => {
      setLoginChallenge(null);
      secondFactorForm.resetFields();
    },
    onVerify2FaSubmit: (values: { otp: string }) => {
      verify2FaMutation.mutate(values);
    },
    onRecover2FaSubmit: (values: Recover2FaInput) => {
      recover2FaMutation.mutate(values);
    },
    onLoginSecondFactorModeChange: (value: SecondFactorMode) => {
      setLoginSecondFactorMode(value);
      if (value === "otp") {
        secondFactorForm.setFieldValue("recoveryCode", undefined);
        return;
      }

      secondFactorForm.setFieldValue("otp", undefined);
    },
  };
}
