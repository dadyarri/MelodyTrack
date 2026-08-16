import { useMutation } from "@tanstack/react-query";
import { App as AntdApp } from "antd";
import { useState } from "react";
import { useSearchParams } from "react-router";

import { authApi, type ResetPasswordInput } from "@/entities/session";
import { getApiErrorMessage, getApiErrorMessages } from "@/shared/api";

export type ResetPasswordSecondFactorMode = "otp" | "recoveryCode";

export function useRestorePasswordPageController() {
  const [searchParams] = useSearchParams();
  const token = searchParams.get("code") ?? "";
  const { message } = AntdApp.useApp();
  const [secondFactorMode, setSecondFactorMode] = useState<ResetPasswordSecondFactorMode>("otp");
  const [submitError, setSubmitError] = useState<string | null>(null);
  const showErrors = (error: unknown) => {
    for (const errorMessage of getApiErrorMessages(error)) {
      void message.error(errorMessage);
    }
  };

  const resetPasswordMutation = useMutation({
    mutationFn: (values: Omit<ResetPasswordInput, "token">) =>
      authApi.resetPassword({
        token,
        newPassword: values.newPassword,
        otp: values.otp,
        recoveryCode: values.recoveryCode,
      }),
    onSuccess: () => {
      setSubmitError(null);
      message.success("Пароль изменен. Теперь можно войти с новым паролем.");
    },
    onError: (error) => {
      setSubmitError(getApiErrorMessage(error));
      showErrors(error);
    },
  });

  return {
    token,
    secondFactorMode,
    setSecondFactorMode,
    submitError,
    resetPasswordMutation,
    onSubmit: (values: Omit<ResetPasswordInput, "token">) => {
      resetPasswordMutation.mutate(values);
    },
  };
}
