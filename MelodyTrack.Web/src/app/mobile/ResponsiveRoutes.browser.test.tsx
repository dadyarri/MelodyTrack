import "../styles/index.css";
import "../styles/mobile-compatibility.css";

import { Button, Card, DatePicker, Form, Input, Select, Space, Table, Tabs, Typography } from "antd";
import dayjs from "dayjs";
import { afterEach, describe, expect, it } from "vitest";
import { page } from "vitest/browser";
import { render } from "vitest-browser-react";

import { ThemeProvider } from "@/shared/config";
import { AuthScreenLayout, ListFilters, ListTable, PageLayout, SummaryCard, SummaryGrid } from "@/shared/ui";
import { filterFieldClassName, filterFieldWideClassName } from "@/shared/ui/filterFieldStyles";
import { AppointmentsCalendar } from "@/widgets/schedule-calendar";

const longRussianLabel = "Очень длинное имя клиента для проверки переноса без перекрытия соседних действий";
const routeFamilies = ["auth", "list", "analytics", "schedule", "workspace", "profile", "portal"] as const;
const portraitViewports = [
  { width: 320, height: 568 },
  { width: 375, height: 667 },
  { width: 390, height: 844 },
  { width: 430, height: 932 },
] as const;

afterEach(() => {
  document.documentElement.dataset.theme = "light";
  document.documentElement.style.fontSize = "";
  localStorage.clear();
});

describe("responsive route families", () => {
  for (const { width, height } of portraitViewports) {
    it.each(routeFamilies)(`keeps the %s family inside a ${String(width)}px portrait viewport`, async (family) => {
      await page.viewport(width, height);
      const screen = await render(<RouteFamilySurface family={family} />);

      await expect.element(screen.getByTestId("route-family-surface")).toBeVisible();
      await assertNoDocumentOverflow();
    });
  }

  it.each(routeFamilies)("keeps the %s family inside a compact landscape viewport", async (family) => {
    await page.viewport(568, 320);
    const screen = await render(<RouteFamilySurface family={family} />);

    await expect.element(screen.getByTestId("route-family-surface")).toBeVisible();
    await assertNoDocumentOverflow();
  });

  it("keeps long labels and validation errors contained with increased text size", async () => {
    await page.viewport(320, 568);
    document.documentElement.style.fontSize = "20px";
    const screen = await render(<RouteFamilySurface family="profile" />);

    await screen.getByRole("button", { name: "Сохранить" }).click();
    await expect.element(screen.getByText("Введите обязательное значение с очень длинным пояснением")).toBeVisible();
    await assertNoDocumentOverflow();
  });

  it.each(portraitViewports)("keeps OTP fields usable at %spx", async ({ width, height }) => {
    await page.viewport(width, height);
    const screen = await render(<OtpSurface />);

    await expect.element(screen.getByLabelText("OTP Input 1", { exact: true }).first()).toBeVisible();
    await assertOtpFieldSize();
    await assertNoDocumentOverflow();
  });

  it("lets the page own vertical scrolling instead of the table", async () => {
    await page.viewport(320, 568);
    const screen = await render(<RouteFamilySurface family="list" />);

    await expect.element(screen.getByText("Клиенты")).toBeVisible();
    await assertTablesDoNotScrollVertically();
    await assertNoDocumentOverflow();
  });

  it("honors the operating system reduced-motion preference", () => {
    expect(window.matchMedia("(prefers-reduced-motion: reduce)").matches).toBe(true);
  });
});

function RouteFamilySurface({ family }: { family: (typeof routeFamilies)[number] }) {
  return (
    <ThemeProvider>
      <div
        data-testid="route-family-surface"
        style={{
          minHeight: "var(--visual-viewport-height)",
          padding: 12,
          background: "var(--bg-app)",
        }}
      >
        {renderRouteFamily(family)}
      </div>
    </ThemeProvider>
  );
}

function renderRouteFamily(family: (typeof routeFamilies)[number]) {
  switch (family) {
    case "auth":
      return (
        <AuthScreenLayout title="MelodyTrack" description={longRussianLabel}>
          <Form layout="vertical">
            <Form.Item label="Электронная почта">
              <Input value="long-address-for-mobile-layout@example-melodytrack.test" readOnly />
            </Form.Item>
            <Button type="primary" block>
              Продолжить
            </Button>
          </Form>
        </AuthScreenLayout>
      );
    case "list":
      return (
        <PageLayout title="Клиенты" description={longRussianLabel} actions={<Button type="primary">Добавить клиента</Button>}>
          <ListFilters>
            <div className={filterFieldClassName}>
              <Input placeholder={longRussianLabel} />
            </div>
            <div className={filterFieldWideClassName}>
              <DatePicker.RangePicker className="wide" />
            </div>
          </ListFilters>
          <ListTable
            rowKey="id"
            pagination={{ pageSize: 1, total: 12 }}
            dataSource={[{ id: "1", name: longRussianLabel, email: "long-address-for-mobile-layout@example-melodytrack.test" }]}
            columns={[
              { title: "Клиент", dataIndex: "name" },
              { title: "Email", dataIndex: "email" },
              { title: "Действия", key: "actions", fixed: "right", render: () => <Button>Открыть</Button> },
            ]}
          />
        </PageLayout>
      );
    case "analytics":
      return (
        <PageLayout title="Аналитика" actions={<DatePicker.RangePicker />}>
          <SummaryGrid>
            <SummaryCard title={longRussianLabel} value="1 234 567,89 ₽" />
            <SummaryCard title="Средний чек" value="12 345,67 ₽" />
          </SummaryGrid>
          <Card title="Динамика показателей">
            <div style={{ width: "100%", overflowX: "auto" }}>
              <svg aria-label="График" width="560" height="180" viewBox="0 0 560 180">
                <title>График</title>
                <path d="M10 150 L140 80 L260 120 L390 30 L550 70" fill="none" stroke="currentColor" strokeWidth="4" />
              </svg>
            </div>
          </Card>
        </PageLayout>
      );
    case "schedule":
      return (
        <PageLayout title="Расписание" actions={<Button type="primary">Новая запись</Button>}>
          <AppointmentsCalendar
            appointments={[]}
            loading={false}
            range={[dayjs("2026-07-20"), dayjs("2026-07-26")]}
            onCreateAt={() => undefined}
            onReschedule={() => undefined}
            onSelect={() => undefined}
            onComplete={() => undefined}
            reschedulePendingAppointmentId={null}
            selectedAppointmentId={null}
          />
        </PageLayout>
      );
    case "workspace":
      return (
        <PageLayout title="Курсы" actions={<Button type="primary">Создать курс</Button>}>
          <Tabs items={[{ key: "editor", label: "Редактирование курса", children: <WorkspaceFixture /> }]} />
        </PageLayout>
      );
    case "profile":
      return (
        <PageLayout title="Профиль" description={longRussianLabel}>
          <Card title="График работы и отпуск">
            <Form layout="vertical">
              <Form.Item
                name="name"
                label={longRussianLabel}
                rules={[{ required: true, message: "Введите обязательное значение с очень длинным пояснением" }]}
              >
                <Input />
              </Form.Item>
              <Form.Item label="Часовой пояс">
                <Select options={[{ value: "Europe/Moscow", label: "Europe/Moscow — Москва и Московская область" }]} />
              </Form.Item>
              <Button type="primary" htmlType="submit">
                Сохранить
              </Button>
            </Form>
          </Card>
        </PageLayout>
      );
    case "portal":
      return (
        <main style={{ width: "min(100%, 720px)", margin: "0 auto" }}>
          <PageLayout title="Портал ученика" description={longRussianLabel} actions={<Button>Выйти из портала</Button>}>
            <SummaryGrid>
              <SummaryCard title="Ближайшее занятие" value="Понедельник, 27 июля, 18:30" />
              <SummaryCard title="Баланс" value="−12 345,67 ₽" />
            </SummaryGrid>
          </PageLayout>
        </main>
      );
  }
}

function WorkspaceFixture() {
  return (
    <Space orientation="vertical" className="wide">
      <Card title={longRussianLabel} extra={<Button>Редактировать</Button>}>
        <Typography.Paragraph>{longRussianLabel.repeat(2)}</Typography.Paragraph>
      </Card>
      <Table
        rowKey="id"
        scroll={{ x: "max-content" }}
        dataSource={[{ id: "theme-1", theme: longRussianLabel, progress: "Выполняется домашнее задание" }]}
        columns={[
          { title: "Тема", dataIndex: "theme" },
          { title: "Прогресс", dataIndex: "progress" },
          { title: "Действие", render: () => <Button>Открыть тему</Button> },
        ]}
      />
    </Space>
  );
}

function OtpSurface() {
  return (
    <ThemeProvider>
      <div style={{ width: "100%", padding: 12 }}>
        <Card title="Введите код">
          <Input.OTP length={6} inputMode="numeric" />
          <Input.OTP length={10} inputMode="text" style={{ marginTop: 16 }} />
        </Card>
      </div>
    </ThemeProvider>
  );
}

async function assertOtpFieldSize() {
  await expect
    .poll(() => {
      const inputs = [...document.querySelectorAll<HTMLElement>(".ant-otp-input")];
      return (
        inputs.length > 0 &&
        inputs.every((input) => input.getBoundingClientRect().width >= 40 && input.getBoundingClientRect().height >= 44)
      );
    })
    .toBe(true);
}

async function assertTablesDoNotScrollVertically() {
  await expect
    .poll(() => {
      const scrollContainers = [...document.querySelectorAll<HTMLElement>(".ant-table-body, .ant-table-content")];
      return (
        scrollContainers.length > 0 &&
        scrollContainers.every((container) => {
          const overflowY = getComputedStyle(container).overflowY;
          return overflowY !== "auto" && overflowY !== "scroll" && container.scrollHeight <= container.clientHeight;
        })
      );
    })
    .toBe(true);
}

async function assertNoDocumentOverflow() {
  await expect.poll(() => document.documentElement.scrollWidth - document.documentElement.clientWidth).toBeLessThanOrEqual(0);
}
