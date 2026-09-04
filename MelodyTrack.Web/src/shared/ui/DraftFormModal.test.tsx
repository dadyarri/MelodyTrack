import { fireEvent, render, screen } from "@testing-library/react";
import type { SyntheticEvent } from "react";
import { describe, expect, it, vi } from "vitest";

import { DraftFormModal } from "./DraftFormModal";

describe("DraftFormModal request lifecycle", () => {
  it("announces pending work, blocks duplicate confirmation, and keeps cancellation available", () => {
    const onOk = vi.fn();
    const onCancel = vi.fn();
    const onSubmit = vi.fn((event: SyntheticEvent) => {
      event.preventDefault();
    });

    render(
      <DraftFormModal
        open
        title="Редактирование"
        restored={false}
        showDraftState={false}
        showClearDraft={false}
        onClearDraft={vi.fn()}
        onOk={onOk}
        onCancel={onCancel}
        confirmLoading
        okText="Сохранить"
        cancelText="Отмена"
      >
        <form aria-label="Форма" onSubmit={onSubmit}>
          <input aria-label="Название" defaultValue="Сохранённое значение" />
        </form>
      </DraftFormModal>,
    );

    expect(screen.getByText("Сохраняем изменения…")).toHaveAttribute("role", "status");
    expect(screen.getByText("Сохраняем изменения…").parentElement).toHaveAttribute("aria-busy", "true");
    expect(screen.getByRole("button", { name: /Сохранить/ })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: /Сохранить/ }));
    fireEvent.submit(screen.getByRole("form", { name: "Форма" }));
    expect(onOk).not.toHaveBeenCalled();
    expect(onSubmit).not.toHaveBeenCalled();

    expect(screen.getByLabelText("Название")).toHaveValue("Сохранённое значение");
    fireEvent.click(screen.getByRole("button", { name: "Отмена" }));
    expect(onCancel).toHaveBeenCalledOnce();
  });

  it("allows exactly one confirmation after pending work completes", () => {
    const onOk = vi.fn();
    const props = {
      open: true,
      title: "Редактирование",
      restored: false,
      showDraftState: false,
      showClearDraft: false,
      onClearDraft: vi.fn(),
      onOk,
      okText: "Сохранить",
    };
    const view = render(
      <DraftFormModal {...props} confirmLoading>
        <span>Данные формы</span>
      </DraftFormModal>,
    );

    view.rerender(
      <DraftFormModal {...props} confirmLoading={false}>
        <span>Данные формы</span>
      </DraftFormModal>,
    );
    fireEvent.click(screen.getByRole("button", { name: /Сохранить/ }));

    expect(onOk).toHaveBeenCalledOnce();
    expect(screen.getByRole("status")).toHaveTextContent("");
  });
});
