import { fireEvent, render as renderUi, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import { Notifications } from "@/shared/ui/notifications";
import { ClientNotesSection } from "./ClientNotesSection";

function render(ui: ReactNode) {
  return renderUi(ui, {
    wrapper: ({ children }) => (
      <>
        {children}
        <Notifications appearance="light" />
      </>
    ),
  });
}

describe("client notes", () => {
  it("keeps a dirty note draft after a server refresh and shows the conflict", () => {
    const { rerender } = render(
      <ClientNotesSection
        notes="Исходная заметка"
        history={[]}
        canEdit
        onSave={vi.fn()}
        onReload={vi.fn()}
      />,
    );
    fireEvent.change(screen.getByLabelText("Заметки клиента"), {
      target: { value: "Черновик" },
    });

    rerender(
      <ClientNotesSection
        notes="Обновлено другим сотрудником"
        history={[]}
        canEdit
        onSave={vi.fn()}
        onReload={vi.fn()}
      />,
    );

    expect((screen.getByLabelText("Заметки клиента") as HTMLTextAreaElement).value).toBe(
      "Черновик",
    );
    expect(screen.getByText("Заметки изменились на сервере.").textContent).toBe(
      "Заметки изменились на сервере.",
    );
    expect((screen.getByRole("button", { name: "Сохранить" }) as HTMLButtonElement).disabled).toBe(
      true,
    );
  });

  it("renders note-clearing history without exposing edit controls to readers", () => {
    render(
      <ClientNotesSection
        notes={null}
        history={[
          {
            id: "history-1",
            authorName: "Сотрудник",
            changedAt: "2026-11-10T10:00:00.000Z",
            before: "Старая заметка",
            after: null,
          },
        ]}
        canEdit={false}
        onSave={vi.fn()}
        onReload={vi.fn()}
      />,
    );

    expect(screen.queryByLabelText("Заметки клиента")).toBeNull();
    expect(screen.queryByText("Старая заметка")).toBeNull();
    const historyButton = screen.getByRole("button", { name: "История изменений заметок" });
    expect(historyButton.getAttribute("aria-expanded")).toBe("false");

    fireEvent.click(historyButton);

    expect(historyButton.getAttribute("aria-expanded")).toBe("true");
    const historyEntry = document.querySelector(".client-note-history-entry");
    expect(historyEntry?.textContent).toContain("Было: Старая заметка");
    expect(historyEntry?.textContent).toContain("Стало: заметки очищены");
    expect(screen.getByText("Сотрудник").textContent).toBe("Сотрудник");

    fireEvent.click(historyButton);

    expect(document.querySelector(".client-note-history-entry")).toBeNull();
  });
});
