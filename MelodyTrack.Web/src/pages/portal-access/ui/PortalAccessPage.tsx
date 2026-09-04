import { useMutation, useQuery } from "@tanstack/react-query";
import { App, Button, Card, Form, Input, Result, Space, Spin, Typography } from "antd";
import { useEffect, useState } from "react";
import { Navigate, useNavigate, useParams } from "react-router";

import { authApi, type ClientPortalPinAuthInput, type SavedClientIdentity, savedClientStorage, useAuth } from "@/entities/session";
import { getApiErrorMessage, getApiFieldErrors, normalizeAppError } from "@/shared/api";
import { AuthScreenLayout } from "@/shared/ui";

type PortalPinFormValues = {
  pin: string;
  pinConfirmation?: string;
};

export function PortalAccessPage() {
  const { token } = useParams<{ token: string }>();

  return <PortalAccessPageContent key={token ?? "saved-clients"} token={token} />;
}

function PortalAccessPageContent({ token }: { token?: string }) {
  const auth = useAuth();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const [savedClients, setSavedClients] = useState(() => savedClientStorage.read());
  const [selectedClient, setSelectedClient] = useState<SavedClientIdentity | null>(null);

  useEffect(
    () =>
      savedClientStorage.subscribe(() => {
        setSavedClients(savedClientStorage.read());
      }),
    [],
  );

  if (auth.isAuthenticated) {
    return <Navigate to={auth.user?.isClientPortal ? "/portal" : "/"} replace />;
  }

  const finishAuthentication = async (response: Awaited<ReturnType<typeof authApi.authenticateClientPortalLink>>) => {
    if (!savedClientStorage.remember(response.savedIdentity)) {
      void message.warning("Вход выполнен, но сохранить профиль в этом браузере не удалось.");
    }
    await auth.establishSession(response.accessToken);
    await navigate("/portal", { replace: true });
  };

  if (token) {
    return <LinkPinForm token={token} onAuthenticated={finishAuthentication} />;
  }

  if (selectedClient) {
    return (
      <SavedClientPinForm
        identity={selectedClient}
        onBack={() => {
          setSelectedClient(null);
        }}
        onForget={() => {
          if (!savedClientStorage.forget(selectedClient.identityId)) {
            void message.error("Не удалось удалить профиль из браузера.");
            return;
          }
          setSelectedClient(null);
        }}
        onAuthenticated={finishAuthentication}
      />
    );
  }

  return (
    <AuthScreenLayout title="Вход на портал ученика">
      {savedClients.length === 0 ? (
        <Result
          status="info"
          title="Сохраненных сессий нет"
          subTitle="Откройте персональную ссылку, которую прислал преподаватель. После первого входа этот браузер запомнит профиль."
        />
      ) : (
        <Space orientation="vertical" size={16} className="wide">
          <div>
            <Typography.Title level={4}>Сохранённые сессии</Typography.Title>
            <Typography.Paragraph type="secondary">Выберите ученика и введите его PIN-код.</Typography.Paragraph>
          </div>
          <Space orientation="vertical" className="wide">
            {savedClients.map((identity) => (
              <Card key={identity.identityId} size="small">
                <Space orientation="vertical" className="wide">
                  <Button
                    type="link"
                    onClick={() => {
                      setSelectedClient(identity);
                    }}
                  >
                    {identity.displayLabel}
                  </Button>
                  <Typography.Text type="secondary">Последний вход: {formatLastUsed(identity.lastUsedAtUtc)}</Typography.Text>
                  <Button
                    type="link"
                    danger
                    onClick={() => {
                      if (!savedClientStorage.forget(identity.identityId)) {
                        void message.error("Не удалось удалить профиль из браузера.");
                      }
                    }}
                  >
                    Забыть
                  </Button>
                </Space>
              </Card>
            ))}
          </Space>
        </Space>
      )}
    </AuthScreenLayout>
  );
}

function SavedClientPinForm({
  identity,
  onBack,
  onForget,
  onAuthenticated,
}: {
  identity: SavedClientIdentity;
  onBack: () => void;
  onForget: () => void;
  onAuthenticated: (response: Awaited<ReturnType<typeof authApi.authenticateSavedClientPortalIdentity>>) => Promise<void>;
}) {
  const [pinError, setPinError] = useState<string | null>(null);
  const statusQuery = useQuery({
    queryKey: ["client-portal", "saved-status", identity.reference],
    queryFn: () => authApi.getSavedClientPortalStatus(identity.reference),
    retry: false,
  });
  const authenticateMutation = useMutation({
    mutationFn: (pin: string) => authApi.authenticateSavedClientPortalIdentity({ reference: identity.reference, pin }),
    onSuccess: onAuthenticated,
    meta: { suppressErrorNotification: true },
    onError: (error) => {
      setPinError(getFieldErrors(getApiFieldErrors(error), "pin")[0] ?? getApiErrorMessage(error));
    },
  });
  const isOfflineFailure = statusQuery.isError && normalizeAppError(statusQuery.error).kind === "network";

  return (
    <AuthScreenLayout title="Вход на портал ученика">
      {statusQuery.isLoading ? (
        <Result status="info" title="Проверяем сохраненный профиль" icon={<Spin size="large" />} />
      ) : statusQuery.isError ? (
        <Result
          status="warning"
          title={isOfflineFailure ? "Нет соединения с сервером" : "Профиль больше недоступен"}
          subTitle={getApiErrorMessage(statusQuery.error)}
          extra={
            isOfflineFailure
              ? [
                  <Button key="retry" type="primary" onClick={() => void statusQuery.refetch()}>
                    Повторить
                  </Button>,
                  <Button key="back" onClick={onBack}>
                    Назад
                  </Button>,
                ]
              : [
                  <Button key="back" onClick={onBack}>
                    Назад
                  </Button>,
                  <Button key="forget" danger onClick={onForget}>
                    Забыть профиль
                  </Button>,
                ]
          }
        />
      ) : (
        <Form<PortalPinFormValues>
          layout="vertical"
          requiredMark={false}
          onValuesChange={() => {
            setPinError(null);
          }}
          onFinish={(values) => {
            authenticateMutation.mutate(values.pin);
          }}
        >
          <Typography.Title level={4}>{statusQuery.data?.displayLabel ?? identity.displayLabel}, введите PIN-код</Typography.Title>
          <Typography.Paragraph type="secondary">Используйте тот же 4-значный PIN.</Typography.Paragraph>
          <PinFormItem error={pinError} />
          <Space orientation="vertical" className="wide">
            <Button block type="primary" htmlType="submit" loading={authenticateMutation.isPending}>
              Войти
            </Button>
            <Button block onClick={onBack}>
              Выбрать другого ученика
            </Button>
          </Space>
        </Form>
      )}
    </AuthScreenLayout>
  );
}

function LinkPinForm({
  token,
  onAuthenticated,
}: {
  token: string;
  onAuthenticated: (response: Awaited<ReturnType<typeof authApi.authenticateClientPortalLink>>) => Promise<void>;
}) {
  const [form] = Form.useForm<PortalPinFormValues>();
  const [pinSetupStep, setPinSetupStep] = useState<"entry" | "confirmation">("entry");
  const [pendingPin, setPendingPin] = useState("");
  const [pinError, setPinError] = useState<string | null>(null);
  const [pinConfirmationError, setPinConfirmationError] = useState<string | null>(null);

  const statusQuery = useQuery({
    queryKey: ["client-portal", "link-status", token],
    queryFn: () => authApi.getClientPortalLinkStatus(token),
    retry: false,
  });
  const authenticateMutation = useMutation({
    mutationFn: (input: ClientPortalPinAuthInput) => authApi.authenticateClientPortalLink(input),
    onSuccess: onAuthenticated,
    meta: { suppressErrorNotification: true },
    onError: (error) => {
      const fieldErrors = getPortalPinFieldErrors(error, statusQuery.data?.hasPin ?? false, pinSetupStep, getApiErrorMessage(error));
      setPinError(fieldErrors.pin);
      setPinConfirmationError(fieldErrors.pinConfirmation);
    },
  });

  return (
    <AuthScreenLayout title="Вход на портал ученика">
      {statusQuery.isLoading ? (
        <Result status="info" title="Проверяем ссылку" icon={<Spin size="large" />} />
      ) : statusQuery.isError ? (
        <Result status="warning" title="Ссылка входа недействительна" subTitle={getApiErrorMessage(statusQuery.error)} />
      ) : statusQuery.data ? (
        <Form<PortalPinFormValues>
          form={form}
          layout="vertical"
          requiredMark={false}
          onValuesChange={(changedValues) => {
            if ("pin" in changedValues) setPinError(null);
            if ("pinConfirmation" in changedValues) setPinConfirmationError(null);
          }}
          onFinish={(values) => {
            if (!statusQuery.data.hasPin && pinSetupStep === "entry") {
              setPendingPin(values.pin);
              setPinSetupStep("confirmation");
              form.setFieldValue("pinConfirmation", undefined);
              return;
            }
            authenticateMutation.mutate({
              token,
              pin: statusQuery.data.hasPin ? values.pin : pendingPin,
              pinConfirmation: statusQuery.data.hasPin ? undefined : values.pinConfirmation,
            });
          }}
        >
          <Typography.Title level={4}>
            {statusQuery.data.firstName},{" "}
            {statusQuery.data.hasPin ? "введите PIN-код" : pinSetupStep === "entry" ? "придумайте PIN-код" : "подтвердите PIN-код"}
          </Typography.Title>
          <Typography.Paragraph type="secondary">
            {statusQuery.data.hasPin
              ? "Используйте тот же 4-значный PIN"
              : pinSetupStep === "entry"
                ? "Это ваш первый вход. Задайте 4-значный PIN — браузер сохранит только безопасный профиль."
                : "Повторите тот же PIN"}
          </Typography.Paragraph>

          {statusQuery.data.hasPin || pinSetupStep === "entry" ? <PinFormItem error={pinError} /> : null}
          {!statusQuery.data.hasPin && pinSetupStep === "confirmation" ? (
            <Form.Item
              name="pinConfirmation"
              label="Подтверждение PIN-кода"
              validateStatus={pinConfirmationError ? "error" : undefined}
              help={pinConfirmationError}
              rules={[
                { required: true, message: "Подтвердите PIN-код" },
                { pattern: /^\d{4}$/, message: "Подтверждение должно состоять из 4 цифр" },
                () => ({
                  validator(_, value) {
                    return !value || pendingPin === value ? Promise.resolve() : Promise.reject(new Error("PIN-коды не совпадают"));
                  },
                }),
              ]}
            >
              <PinCodeInput autoFocus />
            </Form.Item>
          ) : null}

          {!statusQuery.data.hasPin && pinSetupStep === "confirmation" ? (
            <Space orientation="vertical" className="wide">
              <Button
                block
                onClick={() => {
                  setPinSetupStep("entry");
                  setPinConfirmationError(null);
                  form.setFieldValue("pinConfirmation", undefined);
                }}
              >
                Изменить PIN
              </Button>
              <Button block type="primary" htmlType="submit" loading={authenticateMutation.isPending}>
                Сохранить PIN и войти
              </Button>
            </Space>
          ) : (
            <Button block type="primary" htmlType="submit" loading={authenticateMutation.isPending}>
              {statusQuery.data.hasPin ? "Войти" : "Продолжить"}
            </Button>
          )}
        </Form>
      ) : null}
    </AuthScreenLayout>
  );
}

function PinFormItem({ error }: { error: string | null }) {
  return (
    <Form.Item
      name="pin"
      label="PIN-код"
      validateStatus={error ? "error" : undefined}
      help={error}
      rules={[
        { required: true, message: "Введите PIN-код" },
        { pattern: /^\d{4}$/, message: "PIN-код должен состоять из 4 цифр" },
      ]}
    >
      <PinCodeInput autoFocus />
    </Form.Item>
  );
}

function PinCodeInput({ autoFocus = false, value, onChange }: { autoFocus?: boolean; value?: string; onChange?: (value: string) => void }) {
  return (
    <Input.OTP
      length={4}
      autoFocus={autoFocus}
      value={value}
      mask={false}
      inputMode="numeric"
      autoComplete="none"
      aria-autocomplete="none"
      formatter={(next) => next.replace(/\D/g, "")}
      onChange={(next) => onChange?.(next.replace(/\D/g, ""))}
    />
  );
}

function getPortalPinFieldErrors(error: unknown, hasPin: boolean, step: "entry" | "confirmation", fallbackMessage: string) {
  const errorsByField = getApiFieldErrors(error);
  const pinErrors = getFieldErrors(errorsByField, "pin");
  const pinConfirmationErrors = !hasPin ? getFieldErrors(errorsByField, "pinConfirmation") : [];
  const tokenErrors = getFieldErrors(errorsByField, "token");
  const result = { pin: pinErrors[0] ?? null, pinConfirmation: pinConfirmationErrors[0] ?? null };

  if (tokenErrors.length > 0) {
    if (hasPin || step === "entry") result.pin = tokenErrors[0];
    else result.pinConfirmation = tokenErrors[0];
  }
  if (!result.pin && !result.pinConfirmation && fallbackMessage.trim()) {
    if (hasPin || step === "entry") result.pin = fallbackMessage;
    else result.pinConfirmation = fallbackMessage;
  }
  return result;
}

function getFieldErrors(errorsByField: Record<string, string[]>, fieldName: string) {
  return errorsByField[fieldName.toLowerCase()] ?? [];
}

function formatLastUsed(value: string) {
  return new Intl.DateTimeFormat("ru-RU", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
}
