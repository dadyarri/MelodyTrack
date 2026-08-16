import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { CopyTextModal } from "./CopyTextModal";
import { UrlCopyModal } from "./UrlCopyModal";
import { useUrlCopyModal } from "./useUrlCopyModal";

const originalClipboard = Object.getOwnPropertyDescriptor(navigator, "clipboard");

afterEach(() => {
  vi.restoreAllMocks();
  if (originalClipboard) {
    Object.defineProperty(navigator, "clipboard", originalClipboard);
  } else {
    Reflect.deleteProperty(navigator, "clipboard");
  }
});

describe("UrlCopyModal", () => {
  it("renders defaults and keeps a long secret-bearing URL selectable without persisting it", () => {
    const url = `https://example.test/portal/access?token=${"secret-".repeat(40)}`;
    const setItem = vi.spyOn(localStorage, "setItem");

    render(<UrlCopyModal open content={{ url }} onClose={vi.fn()} />);

    expect(screen.getByRole("dialog", { name: "Ссылка готова" })).toBeInTheDocument();
    expect(screen.getByLabelText("Ссылка")).toHaveValue(url);
    expect(screen.getByRole("button", { name: "Скопировать ссылку" })).toBeInTheDocument();
    expect(setItem).not.toHaveBeenCalled();
  });

  it("supports caller copy, labels, warning, confirmation, and close action", async () => {
    const writeText = installClipboardMock().mockResolvedValue();
    const onClose = vi.fn();

    render(
      <UrlCopyModal
        open
        content={{
          url: "https://example.test/calendar.ics",
          title: "Календарь",
          description: "Описание",
          fieldLabel: "Адрес календаря",
          copyButtonLabel: "Копировать календарь",
          copiedConfirmation: "Календарь скопирован",
          closeLabel: "Готово",
          warning: "Старая ссылка отключена",
          context: "Контекст",
        }}
        onClose={onClose}
      />,
    );

    expect(screen.getByText("Описание")).toBeInTheDocument();
    expect(screen.getByText("Контекст")).toBeInTheDocument();
    expect(screen.getByText("Старая ссылка отключена")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Копировать календарь" }));

    expect(writeText).toHaveBeenCalledWith("https://example.test/calendar.ics");
    expect(await screen.findByText("Календарь скопирован")).toHaveAttribute("role", "status");
    fireEvent.click(screen.getByRole("button", { name: "Готово" }));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it("starts clipboard access synchronously from every copy gesture", () => {
    let clickReturned = false;
    const writeText = installClipboardMock().mockImplementation(() => {
      expect(clickReturned).toBe(false);
      return Promise.resolve();
    });

    render(<UrlCopyModal open content={{ url: "https://example.test/link" }} onClose={vi.fn()} />);
    const copyButton = screen.getByRole("button", { name: "Скопировать ссылку" });
    fireEvent.click(copyButton);
    clickReturned = true;

    expect(writeText).toHaveBeenCalledOnce();
    clickReturned = false;
    fireEvent.click(copyButton);
    clickReturned = true;
    expect(writeText).toHaveBeenCalledTimes(2);
  });

  it.each(["unavailable", "rejected"] as const)("keeps the URL visible for manual copy when clipboard is %s", async (failure) => {
    if (failure === "unavailable") {
      Object.defineProperty(navigator, "clipboard", { configurable: true, value: undefined });
    } else {
      installClipboardMock().mockRejectedValue(new DOMException("Raw platform error", "NotAllowedError"));
    }

    render(<UrlCopyModal open content={{ url: "https://example.test/manual" }} onClose={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Скопировать ссылку" }));

    expect(await screen.findByText(/Выделите ссылку и скопируйте её вручную/)).toBeInTheDocument();
    expect(screen.getByLabelText("Ссылка")).toHaveValue("https://example.test/manual");
    expect(screen.queryByText("Raw platform error")).not.toBeInTheDocument();
  });
});

describe("CopyTextModal", () => {
  it("uses generic text labels and reveals a follow-up destination only after copying", async () => {
    const writeText = installClipboardMock().mockResolvedValue();

    render(
      <CopyTextModal
        open
        content={{
          value: "Актуальный текст сообщения",
          followUpAction: {
            label: "Открыть VK",
            href: "https://vk.me/example",
            target: "_blank",
          },
        }}
        onClose={vi.fn()}
      />,
    );

    expect(screen.getByRole("dialog", { name: "Текст готов" })).toBeInTheDocument();
    expect(screen.getByLabelText("Текст")).toHaveValue("Актуальный текст сообщения");
    expect(screen.queryByRole("link", { name: "Открыть VK" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Скопировать" }));

    expect(writeText).toHaveBeenCalledWith("Актуальный текст сообщения");
    expect(await screen.findByText("Текст скопирован")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Открыть VK" })).toHaveAttribute("href", "https://vk.me/example");
  });
});

describe("useUrlCopyModal", () => {
  it("clears the URL on close and immediately hides it when the authenticated owner changes", async () => {
    const { rerender } = render(<UrlModalHarness ownerKey="user-a" />);
    fireEvent.click(screen.getByRole("button", { name: "Создать ссылку" }));
    expect(screen.getByLabelText("Ссылка")).toHaveValue("https://example.test/user-a");

    rerender(<UrlModalHarness ownerKey="user-b" />);
    await waitFor(() => {
      expect(screen.queryByDisplayValue("https://example.test/user-a")).not.toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole("button", { name: "Создать ссылку" }));
    expect(screen.getByLabelText("Ссылка")).toHaveValue("https://example.test/user-b");
    fireEvent.click(screen.getByRole("button", { name: "Закрыть" }));
    await waitFor(() => {
      expect(screen.queryByDisplayValue("https://example.test/user-b")).not.toBeInTheDocument();
    });
  });

  it("discards a URL returned by a request started for the previous account", () => {
    let finishRequest: (() => void) | undefined;
    const schedule = (callback: () => void) => {
      finishRequest = callback;
    };
    const { rerender } = render(<UrlModalHarness ownerKey="user-a" schedule={schedule} />);
    fireEvent.click(screen.getByRole("button", { name: "Запустить создание" }));

    rerender(<UrlModalHarness ownerKey="user-b" schedule={schedule} />);
    act(() => {
      finishRequest?.();
    });

    expect(screen.queryByDisplayValue("https://example.test/user-a")).not.toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});

function installClipboardMock() {
  const writeText = vi.fn<(value: string) => Promise<void>>();
  Object.defineProperty(navigator, "clipboard", {
    configurable: true,
    value: { writeText },
  });
  return writeText;
}

function UrlModalHarness({ ownerKey, schedule }: { ownerKey: string; schedule?: (callback: () => void) => void }) {
  const modal = useUrlCopyModal(ownerKey);
  const [counter, setCounter] = useState(0);
  return (
    <>
      <button
        type="button"
        onClick={() => {
          setCounter((value) => value + 1);
          modal.openUrlModal({ url: `https://example.test/${ownerKey}` });
        }}
      >
        Создать ссылку
      </button>
      <button
        type="button"
        onClick={() => {
          schedule?.(() => {
            modal.openUrlModal({ url: `https://example.test/${ownerKey}` });
          });
        }}
      >
        Запустить создание
      </button>
      <span>{counter}</span>
      <UrlCopyModal {...modal.urlModalProps} />
    </>
  );
}
