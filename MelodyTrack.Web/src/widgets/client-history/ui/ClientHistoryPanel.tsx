import { Button, Card, Descriptions, Empty, List, Pagination, Progress, Space, Tag, Typography } from "antd";

import { getAppointmentStatusTagColor } from "@/entities/appointment";
import {
  type ClientHistory,
  getClientContactValue,
  renderClientHistoryAppointmentStatus,
  renderClientPhoneLink,
  renderClientSocialLink,
} from "@/entities/client";
import type { CourseEnrollment, CourseEnrollmentThemeProgressAction } from "@/entities/course";
import { formatDate, formatDateTime, formatMoney } from "@/shared/lib";
import {
  BookOutlined,
  CalendarCheckOutlined,
  CalendarOutlined,
  CheckOutlined,
  CreditCardOutlined,
  DeleteOutlined,
  DisconnectOutlined,
  LinkOutlined,
  PlusOutlined,
  ReloadOutlined,
} from "@/shared/ui/icons";

import styles from "./ClientHistoryPanel.module.css";

type ClientHistoryPanelProps = {
  data: ClientHistory;
  courseEnrollments?: CourseEnrollment[];
  isCourseEnrollmentsLoading?: boolean;
  isCourseEnrollmentsError?: boolean;
  onCreateAppointment?: (client: ClientHistory["client"]) => void;
  onCreatePayment?: (client: ClientHistory["client"]) => void;
  onCreateCourseEnrollment?: () => void;
  onCreatePortalLink?: () => void;
  isCreatingPortalLink?: boolean;
  onRevokePortalLink?: () => void;
  isRevokingPortalLink?: boolean;
  onCreateCalendarSubscription?: () => void;
  isCreatingCalendarSubscription?: boolean;
  onResetPortalPin?: () => void;
  isResettingPortalPin?: boolean;
  onDeleteCourseEnrollment?: (enrollmentId: string) => void;
  onOpenCourseProgress?: (enrollmentId?: string) => void;
  onUpdateThemeProgress?: (themeId: string, action: CourseEnrollmentThemeProgressAction) => void;
  onEventsPageChange?: (page: number) => void;
  onEditVacations?: (client: ClientHistory["client"]) => void;
};

export function ClientHistoryPanel({
  data,
  courseEnrollments,
  isCourseEnrollmentsLoading = false,
  isCourseEnrollmentsError = false,
  onCreateAppointment,
  onCreatePayment,
  onCreateCourseEnrollment,
  onCreatePortalLink,
  isCreatingPortalLink = false,
  onRevokePortalLink,
  isRevokingPortalLink = false,
  onCreateCalendarSubscription,
  isCreatingCalendarSubscription = false,
  onResetPortalPin,
  isResettingPortalPin = false,
  onDeleteCourseEnrollment,
  onOpenCourseProgress,
  onUpdateThemeProgress,
  onEventsPageChange,
  onEditVacations,
}: ClientHistoryPanelProps) {
  return (
    <Space orientation="vertical" size={16} className="wide">
      {onCreateAppointment ||
      onCreatePayment ||
      onCreateCourseEnrollment ||
      onCreatePortalLink ||
      onRevokePortalLink ||
      onCreateCalendarSubscription ||
      onResetPortalPin ||
      onEditVacations ? (
        <div className={styles.actionToolbar}>
          {onCreateAppointment || onCreatePayment || onCreateCourseEnrollment || onEditVacations ? (
            <div className={styles.actionGroup}>
              <Typography.Text type="secondary" className={styles.actionGroupLabel}>
                Основное
              </Typography.Text>
              <Space wrap size={[8, 8]}>
                {onCreatePayment ? (
                  <Button
                    type="primary"
                    icon={<CreditCardOutlined />}
                    onClick={() => {
                      onCreatePayment(data.client);
                    }}
                  >
                    Добавить платеж
                  </Button>
                ) : null}
                {onCreateAppointment ? (
                  <Button
                    icon={<CalendarOutlined />}
                    onClick={() => {
                      onCreateAppointment(data.client);
                    }}
                  >
                    Записать
                  </Button>
                ) : null}
                {onCreateCourseEnrollment ? (
                  <Button icon={<PlusOutlined />} onClick={onCreateCourseEnrollment}>
                    Назначить курс
                  </Button>
                ) : null}
                {onEditVacations ? (
                  <Button
                    icon={<CalendarOutlined />}
                    onClick={() => {
                      onEditVacations(data.client);
                    }}
                  >
                    Отпуск
                  </Button>
                ) : null}
              </Space>
            </div>
          ) : null}
          {onCreatePortalLink || onRevokePortalLink || onCreateCalendarSubscription || onResetPortalPin ? (
            <div className={`${styles.actionGroup} ${styles.portalActionGroup}`}>
              <Typography.Text type="secondary" className={styles.actionGroupLabel}>
                Клиентский портал
              </Typography.Text>
              <Space wrap size={[8, 8]}>
                {onCreatePortalLink ? (
                  <Button icon={<LinkOutlined />} loading={isCreatingPortalLink} onClick={onCreatePortalLink}>
                    Новая ссылка
                  </Button>
                ) : null}
                {onRevokePortalLink ? (
                  <Button danger icon={<DisconnectOutlined />} loading={isRevokingPortalLink} onClick={onRevokePortalLink}>
                    Отключить ссылку
                  </Button>
                ) : null}
                {onCreateCalendarSubscription ? (
                  <Button icon={<CalendarCheckOutlined />} loading={isCreatingCalendarSubscription} onClick={onCreateCalendarSubscription}>
                    Создать ссылку календаря
                  </Button>
                ) : null}
                {onResetPortalPin ? (
                  <Button icon={<ReloadOutlined />} loading={isResettingPortalPin} onClick={onResetPortalPin}>
                    Сбросить PIN
                  </Button>
                ) : null}
              </Space>
            </div>
          ) : null}
        </div>
      ) : null}
      <div className={styles.detailGrid}>
        <Card size="small">
          <Descriptions size="small" title="Контакты" column={1}>
            <Descriptions.Item label="Телефон">
              {renderClientPhoneLink(getClientContactValue(data.client, "phone")) || "Не указан"}
            </Descriptions.Item>
            <Descriptions.Item label="Telegram">
              {renderClientSocialLink(getClientContactValue(data.client, "telegram"), "telegram") || "Не указан"}
            </Descriptions.Item>
            <Descriptions.Item label="VK">
              {renderClientSocialLink(getClientContactValue(data.client, "vk"), "vk") || "Не указан"}
            </Descriptions.Item>
            <Descriptions.Item label="Источник">{data.client.sourceName || "Не указан"}</Descriptions.Item>
            <Descriptions.Item label="Дата рождения">
              {data.client.dateOfBirth ? formatDate(data.client.dateOfBirth) : "Не указана"}
            </Descriptions.Item>
          </Descriptions>
        </Card>
        <Card size="small">
          <Descriptions size="small" title="Сводка" column={1}>
            <Descriptions.Item label="Баланс">
              <Tag color={data.client.balance < 0 ? "red" : "green"}>{formatMoney(data.client.balance)}</Tag>
            </Descriptions.Item>
            <Descriptions.Item label="Платежей">{data.summary.paymentsCount}</Descriptions.Item>
            <Descriptions.Item label="Всего оплачено">{formatMoney(data.summary.totalPayments)}</Descriptions.Item>
            <Descriptions.Item label="Завершенных визитов">{data.summary.completedAppointmentsCount}</Descriptions.Item>
            <Descriptions.Item label="Будущих записей">{data.summary.upcomingAppointmentsCount}</Descriptions.Item>
            <Descriptions.Item label="Последний платеж">{formatOptionalDateTime(data.summary.lastPaymentAtUtc)}</Descriptions.Item>
            <Descriptions.Item label="Последний визит">{formatOptionalDateTime(data.summary.lastVisitAtUtc)}</Descriptions.Item>
            <Descriptions.Item label="Следующая запись">{formatOptionalDateTime(data.summary.nextAppointmentAtUtc)}</Descriptions.Item>
          </Descriptions>
        </Card>
      </div>

      <Card
        size="small"
        title={
          <Space size={8}>
            <BookOutlined />
            <span>Курсы и прогресс</span>
          </Space>
        }
      >
        {isCourseEnrollmentsLoading ? <Typography.Text type="secondary">Загрузка прогресса...</Typography.Text> : null}
        {isCourseEnrollmentsError ? <Typography.Text type="danger">Не удалось загрузить прогресс по курсам.</Typography.Text> : null}
        {!isCourseEnrollmentsLoading && !isCourseEnrollmentsError && (courseEnrollments?.length ?? 0) === 0 ? (
          <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Курсы пока не назначены" />
        ) : null}
        {!isCourseEnrollmentsLoading && !isCourseEnrollmentsError && (courseEnrollments?.length ?? 0) > 0 ? (
          <Space orientation="vertical" size={12} className="wide">
            {courseEnrollments?.map((enrollment) => (
              <Card
                key={enrollment.id}
                size="small"
                className={styles.enrollmentCard}
                title={enrollment.courseName}
                extra={
                  <Space wrap size={8}>
                    {onOpenCourseProgress ? (
                      <Button
                        size="small"
                        onClick={() => {
                          onOpenCourseProgress(enrollment.id);
                        }}
                      >
                        Прогресс
                      </Button>
                    ) : null}
                    {onDeleteCourseEnrollment ? (
                      <Button
                        size="small"
                        danger
                        icon={<DeleteOutlined />}
                        onClick={() => {
                          onDeleteCourseEnrollment(enrollment.id);
                        }}
                      >
                        Снять
                      </Button>
                    ) : null}
                  </Space>
                }
              >
                <Space orientation="vertical" size={10} className="wide">
                  <Progress
                    percent={
                      enrollment.themes.length === 0
                        ? 0
                        : Math.round((enrollment.themes.filter((theme) => theme.state === 5).length / enrollment.themes.length) * 100)
                    }
                    size="small"
                  />
                  <Space wrap size={[8, 8]}>
                    {enrollment.themes
                      .filter((theme) => theme.state !== 5)
                      .map((theme) => (
                        <Tag key={theme.id} color={theme.state === 4 ? "orange" : "blue"}>
                          {theme.themeTitle}
                          {onUpdateThemeProgress && theme.state === 4 ? (
                            <Button
                              type="link"
                              size="small"
                              icon={<CheckOutlined />}
                              onClick={() => {
                                onUpdateThemeProgress(theme.id, "pass-homework");
                              }}
                            >
                              Принять ДЗ
                            </Button>
                          ) : null}
                        </Tag>
                      ))}
                  </Space>
                </Space>
              </Card>
            ))}
          </Space>
        ) : null}
      </Card>

      <Card size="small" title="Финансовая история">
        {data.events.items.length > 0 ? (
          <Space orientation="vertical" size={16} className="wide">
            <List
              dataSource={data.events.items}
              renderItem={(event) => (
                <List.Item>
                  <div className="wide">
                    <Space className={`wide ${styles.listJustify}`} wrap>
                      <Typography.Text strong>{event.type === "top_up" ? "Пополнение" : event.serviceName}</Typography.Text>
                      <Space wrap size={8}>
                        {event.appointmentStatus ? (
                          <Tag color={getAppointmentStatusTagColor(event.appointmentStatus)}>
                            {renderClientHistoryAppointmentStatus(event.appointmentStatus)}
                          </Tag>
                        ) : null}
                        <Typography.Text strong type={event.amount < 0 ? "danger" : "success"}>
                          {event.amount > 0 ? "+" : ""}
                          {formatMoney(event.amount)}
                        </Typography.Text>
                        <Typography.Text type="secondary">{formatDateTime(event.date)}</Typography.Text>
                      </Space>
                    </Space>
                    {event.type === "top_up" ? (
                      <Typography.Text>{event.description?.trim() || "Без описания"}</Typography.Text>
                    ) : (
                      <Typography.Text type="secondary">
                        {event.providerDisplayName ? `Преподаватель: ${event.providerDisplayName}` : "Преподаватель не назначен"}
                      </Typography.Text>
                    )}
                  </div>
                </List.Item>
              )}
            />
            {data.events.page.total > data.events.page.pageSize ? (
              <Pagination
                align="end"
                current={data.events.page.page}
                pageSize={data.events.page.pageSize}
                total={data.events.page.total}
                onChange={onEventsPageChange}
                showSizeChanger={false}
              />
            ) : null}
          </Space>
        ) : (
          <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Финансовых событий пока нет" />
        )}
      </Card>
    </Space>
  );
}

function formatOptionalDateTime(value?: string | null) {
  return value ? formatDateTime(value) : "Нет данных";
}
