import "../styles/index.css";
import "../styles/mobile-compatibility.css";

import { Button, DatePicker, Drawer, Dropdown, Form, Input, Modal, Select, Space } from "antd";
import { useState } from "react";
import { describe, expect, it } from "vitest";
import { page, userEvent } from "vitest/browser";
import { render } from "vitest-browser-react";

import { ThemeProvider } from "@/shared/config";

import { useMobileOverlayKeyboardPolicy } from "../lib/useMobileOverlayKeyboardPolicy";
import { useVisualViewportCssVariables } from "../lib/useVisualViewportCssVariables";

describe("mobile WebKit overlays", () => {
  it("keeps OTP and recovery-code fields large enough to use", async () => {
    await page.viewport(320, 568);
    const screen = await render(<OtpHarness />);

    await expect.element(screen.getByLabelText("OTP Input 1", { exact: true }).first()).toBeVisible();
    await assertOtpFieldSize();
    await assertNoDocumentOverflow();
  });

  it("keeps modal form controls, picker, select, and actions operable", async () => {
    await page.viewport(320, 568);
    const screen = await render(<OverlayHarness />);

    await screen.getByRole("button", { name: "Открыть форму" }).click();
    await expect.element(screen.getByRole("dialog")).toBeVisible();
    await screen.getByLabelText("Название").fill("Проверка WebKit");
    await expect.element(screen.getByLabelText("Категория")).toHaveAttribute("readonly");
    await expect.element(screen.getByLabelText("Категория")).toHaveAttribute("inputmode", "none");
    await screen.getByLabelText("Категория").click();
    await expect.element(page.getByText("Очень длинная категория для мобильного меню")).toBeVisible();
    assertActivePopupInsideVisualViewport(".ant-select-dropdown:not(.ant-select-dropdown-hidden)");
    await page.getByText("Очень длинная категория для мобильного меню").click();
    await expect.element(screen.getByLabelText("Дата", { exact: true })).toHaveAttribute("readonly");
    await expect.element(screen.getByLabelText("Дата", { exact: true })).toHaveAttribute("inputmode", "none");
    await screen.getByLabelText("Дата", { exact: true }).click();
    await expect.element(page.getByText("Сегодня")).toBeVisible();
    assertActivePopupInsideVisualViewport(".ant-picker-dropdown:not(.ant-picker-dropdown-hidden)");
    await userEvent.keyboard("{Escape}");
    await expect.element(screen.getByRole("button", { name: "Сохранить" })).toBeVisible();
    await assertNoDocumentOverflow();
  });

  it("keeps a date-time picker operable without an editable mobile input", async () => {
    await page.viewport(320, 568);
    const screen = await render(<DateTimeOverlayHarness />);

    await screen.getByRole("button", { name: "Открыть дату и время" }).click();
    await expect.element(screen.getByRole("dialog")).toBeVisible();
    await expect.element(screen.getByPlaceholder("Выберите дату")).toHaveAttribute("readonly");
    await expect.element(screen.getByPlaceholder("Выберите дату")).toHaveAttribute("inputmode", "none");
    await screen.getByPlaceholder("Выберите дату").click();
    await assertElementVisible(".ant-picker-datetime-panel .ant-picker-date-panel");
    await assertElementVisible(".ant-picker-datetime-panel .ant-picker-time-panel");
    assertActivePopupInsideVisualViewport(".ant-picker-dropdown:not(.ant-picker-dropdown-hidden)");
    await assertNoDocumentOverflow();
  });

  it.each([
    [320, 568],
    [568, 320],
  ] as const)("keeps drawers and dropdown menus inside a %sx%s viewport", async (width, height) => {
    await page.viewport(width, height);
    const screen = await render(<OverlayHarness />);

    await screen.getByRole("button", { name: "Открыть меню" }).click();
    await expect.element(page.getByText("Длинный пункт выпадающего меню для проверки переноса")).toBeVisible();
    await userEvent.keyboard("{Escape}");
    await screen.getByRole("button", { name: "Открыть панель" }).click();
    await expect.element(screen.getByText("Мобильная панель")).toBeVisible();
    await screen.getByLabelText("Поле внутри панели").fill("Проверка доступности панели");
    await assertNoDocumentOverflow();
  });
});

function OtpHarness() {
  return (
    <ThemeProvider>
      <main style={{ padding: 12 }}>
        <Input.OTP length={6} inputMode="numeric" />
        <Input.OTP length={10} inputMode="text" style={{ marginTop: 16 }} />
      </main>
    </ThemeProvider>
  );
}

function OverlayHarness() {
  useMobileOverlayKeyboardPolicy();
  useVisualViewportCssVariables();
  const [modalOpen, setModalOpen] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);

  return (
    <ThemeProvider>
      <main style={{ padding: 12 }}>
        <Space wrap>
          <Button
            onClick={() => {
              setModalOpen(true);
            }}
          >
            Открыть форму
          </Button>
          <Dropdown menu={{ items: [{ key: "long", label: "Длинный пункт выпадающего меню для проверки переноса" }] }} trigger={["click"]}>
            <Button>Открыть меню</Button>
          </Dropdown>
          <Button
            onClick={() => {
              setDrawerOpen(true);
            }}
          >
            Открыть панель
          </Button>
        </Space>
        <Modal
          open={modalOpen}
          title="Форма"
          okText="Сохранить"
          onCancel={() => {
            setModalOpen(false);
          }}
        >
          <Form layout="vertical">
            <Form.Item name="name" label="Название">
              <Input />
            </Form.Item>
            <Form.Item name="category" label="Категория">
              <Select showSearch options={[{ value: "long", label: "Очень длинная категория для мобильного меню" }]} />
            </Form.Item>
            <Form.Item name="date" label="Дата">
              <DatePicker className="wide" />
            </Form.Item>
          </Form>
        </Modal>
        <Drawer
          title="Мобильная панель"
          open={drawerOpen}
          size={296}
          onClose={() => {
            setDrawerOpen(false);
          }}
        >
          <Input aria-label="Поле внутри панели" />
        </Drawer>
      </main>
    </ThemeProvider>
  );
}

function DateTimeOverlayHarness() {
  useMobileOverlayKeyboardPolicy();
  useVisualViewportCssVariables();
  const [open, setOpen] = useState(false);

  return (
    <ThemeProvider>
      <main style={{ padding: 12 }}>
        <Button
          onClick={() => {
            setOpen(true);
          }}
        >
          Открыть дату и время
        </Button>
        <Modal
          open={open}
          title="Дата и время"
          footer={null}
          onCancel={() => {
            setOpen(false);
          }}
        >
          <Form layout="vertical">
            <Form.Item name="dateTime" label="Дата и время">
              <DatePicker showTime={{ format: "HH:mm" }} className="wide" />
            </Form.Item>
          </Form>
        </Modal>
      </main>
    </ThemeProvider>
  );
}

async function assertNoDocumentOverflow() {
  await expect.poll(() => document.documentElement.scrollWidth - document.documentElement.clientWidth).toBeLessThanOrEqual(0);
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

async function assertElementVisible(selector: string) {
  const element = document.querySelector<HTMLElement>(selector);
  expect(element).not.toBeNull();
  await expect.element(page.elementLocator(element as Element)).toBeVisible();
}

function assertActivePopupInsideVisualViewport(selector: string) {
  const popup = document.querySelector<HTMLElement>(selector);
  expect(popup).not.toBeNull();

  const bounds = popup?.getBoundingClientRect() ?? new DOMRect(Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY, 0, 0);
  const viewport = window.visualViewport;
  const width = viewport?.width ?? window.innerWidth;
  const height = viewport?.height ?? window.innerHeight;
  const metrics = JSON.stringify({ bottom: bounds.bottom, height, left: bounds.left, right: bounds.right, top: bounds.top, width });
  expect(bounds.left, metrics).toBeGreaterThanOrEqual(0);
  expect(bounds.top, metrics).toBeGreaterThanOrEqual(0);
  expect(Math.ceil(bounds.right), metrics).toBeLessThanOrEqual(Math.floor(width));
  expect(Math.ceil(bounds.bottom), metrics).toBeLessThanOrEqual(Math.floor(height));
}
