import { Alert, Button, Card, DatePicker, Divider, Form, Input, List, Space, Switch, Tag, TimePicker, Typography } from "antd";

import { formatPhoneInput, isValidPhone, normalizeSocialLink } from "@/entities/client";
import type { MeResponse } from "@/entities/session";
import { RecoveryCodesCard, TotpSecretPanel } from "@/entities/session";
import { weekdayLabels, weekdayOrder } from "@/entities/user";
import { DraftModalTitle, PageLayout, ShortcutButton, UrlCopyModal } from "@/shared/ui";
import { DisconnectOutlined, LogoutOutlined, ReloadOutlined, SafetyCertificateOutlined } from "@/shared/ui/icons";

import { type AvailabilityFormValues, type PersonalInfoFormValues, useProfilePageController } from "../model/useProfilePageController";
import styles from "./ProfilePage.module.css";

function getWorkingHoursValue(values: AvailabilityFormValues | undefined, index: number) {
  return values?.workingHours[index]?.isWorkingDay;
}

function getIsWorkingDayFieldValue(form: ReturnType<typeof Form.useForm<AvailabilityFormValues>>[0], index: number) {
  return Boolean(form.getFieldValue(["workingHours", index, "isWorkingDay"]) as boolean | undefined);
}

export function ProfilePage() {
  const controller = useProfilePageController();

  return (
    <PageLayout title="Профиль" description="Управление паролем, 2FA, кодами восстановления и активными сессиями." size={20}>
      <div className="profile-grid">
        <div data-onboarding-id="profile-account-card">
          <Card
            title={
              <DraftModalTitle
                title="Аккаунт"
                restored={controller.personalInfoDraft.restored}
                saveStatus={controller.personalInfoDraft.status}
                onRetry={controller.personalInfoDraft.retry}
              />
            }
            extra={
              controller.personalInfoDraft.hasDraft ? (
                <Button onClick={() => void controller.discardPersonalInfoDraft()}>Отбросить черновик</Button>
              ) : null
            }
          >
            {controller.me ? (
              <Form<PersonalInfoFormValues>
                form={controller.personalInfoForm}
                layout="vertical"
                requiredMark={false}
                onFinish={controller.onPersonalInfoSubmit}
                onValuesChange={controller.personalInfoDraft.formProps.onValuesChange}
              >
                <Space orientation="vertical" size={16} className="wide">
                  <ProfileSummary me={controller.me} />
                  <Form.Item name="lastName" label="Фамилия" rules={[{ required: true }]}>
                    <Input />
                  </Form.Item>
                  <Form.Item name="firstName" label="Имя" rules={[{ required: true }]}>
                    <Input />
                  </Form.Item>
                  <Form.Item
                    name="phone"
                    label="Телефон"
                    rules={[
                      {
                        validator: (_, value?: string) =>
                          !value?.trim() || isValidPhone(value)
                            ? Promise.resolve()
                            : Promise.reject(new Error("Введите корректный номер телефона")),
                      },
                    ]}
                  >
                    <PhoneInput />
                  </Form.Item>
                  <Form.Item
                    name="telegram"
                    label="Telegram"
                    rules={[
                      {
                        validator: (_, value?: string) =>
                          !value?.trim() || normalizeSocialLink(value, "telegram")
                            ? Promise.resolve()
                            : Promise.reject(new Error("Введите Telegram как @nickname, nickname или ссылку t.me")),
                      },
                    ]}
                  >
                    <Input placeholder="@nickname или https://t.me/nickname" />
                  </Form.Item>
                  <Form.Item
                    name="vk"
                    label="VK"
                    rules={[
                      {
                        validator: (_, value?: string) =>
                          !value?.trim() || normalizeSocialLink(value, "vk")
                            ? Promise.resolve()
                            : Promise.reject(new Error("Введите VK как nickname или ссылку vk.com/vk.ru")),
                      },
                    ]}
                  >
                    <Input placeholder="nickname или https://vk.com/nickname" />
                  </Form.Item>
                  <div>
                    <Button type="primary" htmlType="submit" loading={controller.savePersonalInfoMutation.isPending}>
                      Сохранить профиль
                    </Button>
                  </div>
                </Space>
              </Form>
            ) : (
              <Typography.Text type="secondary">Загрузка...</Typography.Text>
            )}
          </Card>
        </div>
        <Card
          data-onboarding-id="profile-onboarding-reset"
          title="Знакомство с MelodyTrack"
          extra={
            <Button icon={<ReloadOutlined />} loading={controller.resetOnboardingMutation.isPending} onClick={controller.resetOnboarding}>
              Пройти ещё раз
            </Button>
          }
        >
          <Typography.Text type="secondary">Короткая экскурсия напомнит об основных возможностях для вашей роли.</Typography.Text>
        </Card>
      </div>

      <Card title="Подписка на календарь">
        <Space orientation="vertical" size={12}>
          <Typography.Text type="secondary">
            Ссылка содержит ваше расписание. При создании новой ссылки предыдущая сразу перестанет работать.
          </Typography.Text>
          <Button
            loading={controller.calendarSubscriptionMutation.isPending}
            disabled={!controller.me}
            onClick={() => {
              if (controller.me) controller.calendarSubscriptionMutation.mutate(controller.me.id);
            }}
          >
            Создать ссылку
          </Button>
        </Space>
      </Card>
      <UrlCopyModal {...controller.urlModalProps} />

      <Card
        title={
          <DraftModalTitle
            title="График работы и отпуск"
            restored={controller.availabilityDraft.restored}
            saveStatus={controller.availabilityDraft.status}
            onRetry={controller.availabilityDraft.retry}
          />
        }
        extra={
          controller.availabilityDraft.hasDraft ? (
            <Button onClick={() => void controller.discardAvailabilityDraft()}>Отбросить черновик</Button>
          ) : null
        }
        loading={controller.availabilityQuery.isLoading}
        data-onboarding-id="profile-availability"
      >
        <Form<AvailabilityFormValues>
          form={controller.availabilityForm}
          layout="vertical"
          requiredMark={false}
          onFinish={controller.onAvailabilitySubmit}
          onValuesChange={controller.availabilityDraft.formProps.onValuesChange}
        >
          <Space orientation="vertical" size={18} className="wide">
            <div>
              <Typography.Title level={5}>Рабочие часы</Typography.Title>
              <Typography.Text type="secondary">Выходные и нерабочие дни выключаются переключателем.</Typography.Text>
            </div>
            <Form.List name="workingHours">
              {(fields) => (
                <Space orientation="vertical" size={12} className="wide">
                  {fields.map((field, index) => (
                    <div className={styles.availabilityRow} key={field.key}>
                      <Form.Item name={[field.name, "dayOfWeek"]} hidden>
                        <Input />
                      </Form.Item>
                      <Typography.Text className={styles.availabilityDayLabel}>{weekdayLabels[weekdayOrder[index]]}</Typography.Text>
                      <Form.Item name={[field.name, "isWorkingDay"]} valuePropName="checked" className={styles.availabilityToggle}>
                        <Switch checkedChildren="Рабочий" unCheckedChildren="Выходной" />
                      </Form.Item>
                      <Form.Item
                        shouldUpdate={(prevValues: AvailabilityFormValues | undefined, nextValues: AvailabilityFormValues | undefined) =>
                          getWorkingHoursValue(prevValues, index) !== getWorkingHoursValue(nextValues, index)
                        }
                        noStyle
                      >
                        {() => {
                          const isWorkingDay = getIsWorkingDayFieldValue(controller.availabilityForm, index);
                          return (
                            <Form.Item
                              name={[field.name, "timeRange"]}
                              className={styles.availabilityTimeRange}
                              rules={
                                isWorkingDay
                                  ? [
                                      {
                                        required: true,
                                        message: "Укажите рабочее время.",
                                      },
                                    ]
                                  : []
                              }
                            >
                              <TimePicker.RangePicker className="wide" format="HH:mm" minuteStep={15} disabled={!isWorkingDay} />
                            </Form.Item>
                          );
                        }}
                      </Form.Item>
                    </div>
                  ))}
                </Space>
              )}
            </Form.List>

            <Divider className={styles.divider} />

            <div className={styles.vacationsHeader}>
              <div>
                <Typography.Title level={5}>Отпуска</Typography.Title>
                <Typography.Text type="secondary">Периоды отпуска блокируют создание и перенос записей.</Typography.Text>
              </div>
              <Button onClick={controller.addVacationDraft}>Добавить отпуск</Button>
            </div>
            <Form.List name="vacations">
              {(fields, { remove }) => (
                <Space orientation="vertical" size={12} className="wide">
                  {fields.length === 0 ? <Typography.Text type="secondary">Отпуска пока не добавлены.</Typography.Text> : null}
                  {fields.map((field) => (
                    <div className={styles.vacationRow} key={field.key}>
                      <Form.Item
                        name={[field.name, "period"]}
                        className={styles.vacationRange}
                        rules={[
                          {
                            required: true,
                            message: "Укажите период отпуска.",
                          },
                        ]}
                      >
                        <DatePicker.RangePicker className="wide" format="DD.MM.YYYY" />
                      </Form.Item>
                      <Button
                        danger
                        onClick={() => {
                          remove(field.name);
                        }}
                      >
                        Удалить
                      </Button>
                    </div>
                  ))}
                </Space>
              )}
            </Form.List>

            <div>
              <Button type="primary" htmlType="submit" loading={controller.saveAvailabilityMutation.isPending}>
                Сохранить график
              </Button>
            </div>
          </Space>
        </Form>
      </Card>

      <div className="profile-grid" data-onboarding-id="profile-security">
        <Card title="Смена пароля">
          <Form layout="vertical" onFinish={controller.onPasswordSubmit} requiredMark={false}>
            <Form.Item name="currentPassword" label="Текущий пароль" rules={[{ required: true }]}>
              <Input.Password autoComplete="current-password" />
            </Form.Item>
            <Form.Item name="newPassword" label="Новый пароль" rules={[{ required: true }]}>
              <Input.Password autoComplete="new-password" />
            </Form.Item>
            <Button type="primary" htmlType="submit" loading={controller.changePasswordMutation.isPending}>
              Сменить пароль
            </Button>
          </Form>
        </Card>

        <Card title="2FA и коды восстановления">
          <Space orientation="vertical" className="wide">
            <Typography.Text>
              {controller.isTwoFactorEnabled
                ? "Вход через приложение-аутентификатор уже включен."
                : "Подключите приложение-аутентификатор и сохраните коды восстановления."}
            </Typography.Text>
            {controller.isTwoFactorRequired ? (
              <Alert type="info" showIcon title="Для этой роли двухфакторная защита обязательна, поэтому отключить ее нельзя." />
            ) : null}
            <Space wrap>
              <ShortcutButton
                shortcut="R"
                disabled={!controller.isTwoFactorEnabled}
                loading={controller.getRecoveryCodesMutation.isPending}
                label="Показать коды"
                onClick={controller.showRecoveryCodes}
              />
              <ShortcutButton
                shortcut="G"
                leadingIcon={<ReloadOutlined />}
                disabled={!controller.isTwoFactorEnabled}
                loading={controller.regenerateRecoveryCodesMutation.isPending}
                label="Перегенерировать коды"
                onClick={controller.regenerateRecoveryCodes}
              />
              <ShortcutButton
                shortcut="O"
                danger
                disabled={!controller.isTwoFactorEnabled || controller.isTwoFactorRequired}
                label="Отключить 2FA"
                onClick={controller.remove2Fa}
              />
            </Space>
            {!controller.isTwoFactorEnabled ? (
              <Form layout="vertical" onFinish={controller.onSetup2FaSubmit} requiredMark={false}>
                <Form.Item name="password" label="Подтвердите текущий пароль" rules={[{ required: true }]}>
                  <Input.Password autoComplete="current-password" />
                </Form.Item>
                <ShortcutButton
                  shortcut="F"
                  type="primary"
                  leadingIcon={<SafetyCertificateOutlined />}
                  htmlType="submit"
                  loading={controller.setup2FaMutation.isPending}
                  label="Получить QR-код и секрет"
                />
              </Form>
            ) : null}
            {controller.setupState ? (
              <Card size="small" className={styles.setupCard}>
                <TotpSecretPanel
                  alertType="info"
                  alertMessage="Подтвердите подключение 2FA"
                  alertDescription="Отсканируйте QR-код в приложении-аутентификаторе, затем подтвердите одноразовый код, чтобы получить коды восстановления."
                  qrValue={controller.setupState.otpUrl}
                  secret={controller.setupState.secret}
                  qrSize={180}
                >
                  <Form layout="vertical" onFinish={controller.onVerify2FaSubmit} requiredMark={false}>
                    <Form.Item name="otp" label="Код из приложения" rules={[{ required: true }]}>
                      <Input inputMode="numeric" autoComplete="one-time-code" />
                    </Form.Item>
                    <Button type="primary" htmlType="submit" loading={controller.verify2FaMutation.isPending}>
                      Подтвердить и получить коды восстановления
                    </Button>
                  </Form>
                </TotpSecretPanel>
              </Card>
            ) : null}
            {controller.recoveryCodes ? (
              <RecoveryCodesCard
                items={controller.recoveryCodes}
                downloadFileName={`MelodyTrackRecovery_${toRecoveryFileStem(controller.me)}.txt`}
              />
            ) : null}
          </Space>
        </Card>
      </div>

      <Card
        title="Активные сессии"
        extra={
          <ShortcutButton
            shortcut="W"
            danger
            leadingIcon={<LogoutOutlined />}
            loading={controller.logoutAllMutation.isPending}
            label="Выйти везде"
            onClick={controller.logoutAll}
          />
        }
      >
        <List
          dataSource={controller.sessionsQuery.data?.data ?? []}
          locale={{ emptyText: "Нет активных сессий" }}
          renderItem={(session) => (
            <List.Item
              actions={[
                <Button
                  key="revoke"
                  danger
                  type="text"
                  icon={<DisconnectOutlined />}
                  loading={controller.revokeSessionMutation.isPending && controller.revokeSessionMutation.variables.id === session.id}
                  onClick={() => {
                    controller.revokeSession(session);
                  }}
                >
                  {session.isCurrent ? "Выйти" : "Завершить"}
                </Button>,
              ]}
            >
              <Space orientation="vertical" size={2}>
                <Space size={8} wrap>
                  <Typography.Text>{session.deviceInfo || "Неизвестное устройство"}</Typography.Text>
                  {session.isCurrent ? <Tag color="green">Текущая</Tag> : null}
                </Space>
                <Typography.Text type="secondary">Последняя активность: {formatSessionTime(session.lastSeenAtUtc)}</Typography.Text>
              </Space>
            </List.Item>
          )}
        />
      </Card>
    </PageLayout>
  );
}

function PhoneInput({ value, onChange }: { value?: string | null; onChange?: (value?: string) => void }) {
  return (
    <Input
      value={value ?? ""}
      inputMode="tel"
      autoComplete="tel"
      placeholder="+49 1512 3456789"
      onChange={(event) => {
        onChange?.(formatPhoneInput(event.target.value) || undefined);
      }}
    />
  );
}

function ProfileSummary({ me }: { me: MeResponse }) {
  return (
    <Space orientation="vertical">
      <Typography.Text strong>
        {me.firstName} {me.lastName}
      </Typography.Text>
      <Typography.Text>{me.email}</Typography.Text>
      <Space wrap>
        <Tag color="gold">{me.roleDisplayName}</Tag>
        <Tag color={me.isTwoFactorEnabled ? "green" : "default"}>{me.isTwoFactorEnabled ? "2FA включен" : "2FA выключен"}</Tag>
      </Space>
    </Space>
  );
}

function toRecoveryFileStem(me?: MeResponse) {
  const source = me?.email ? me.email.split("@")[0] : [me?.firstName, me?.lastName].filter(Boolean).join("_");
  const stem = source
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9._-]+/g, "_");
  return stem || "user";
}

function formatSessionTime(value: string) {
  return new Intl.DateTimeFormat("ru-RU", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}
