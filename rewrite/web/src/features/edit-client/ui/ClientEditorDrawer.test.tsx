import { fireEvent, render as renderUi, screen, waitFor, within } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { DirectoryClient } from "@/entities/client-directory";
import { Notifications } from "@/shared/ui/notifications";
import { ClientEditorDrawer } from "./ClientEditorDrawer";

const offlineStorageMock = vi.hoisted(() => ({ value: null as unknown }));
vi.mock("@/entities/offline-storage", () => ({
  useOfflineStorage: () => offlineStorageMock.value,
}));
afterEach(() => {
  offlineStorageMock.value = null;
});

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

const kinds = [
  { id: "phone", name: "Телефон", format: "phone" as const, isArchived: false },
  { id: "email", name: "Email", format: "email" as const, isArchived: false },
];
const sources = [{ id: "source", name: "Рекомендация" }];

function client(contacts: DirectoryClient["contacts"]): DirectoryClient {
  return {
    id: "client",
    firstName: "Анна",
    lastName: "Иванова",
    middleName: null,
    birthdate: null,
    sourceId: "source",
    sourceName: "Рекомендация",
    isArchived: false,
    createdAt: "2026-10-01T00:00:00.000Z",
    contacts,
  };
}

const phoneContacts: DirectoryClient["contacts"] = [
  {
    id: "phone-a",
    kindId: "phone",
    kindName: "Телефон",
    format: "phone",
    value: "+7 900 000-00-01",
    comment: null,
    isPrimary: true,
  },
  {
    id: "phone-b",
    kindId: "phone",
    kindName: "Телефон",
    format: "phone",
    value: "+7 900 000-00-02",
    comment: null,
    isPrimary: false,
  },
];

describe("client editor", () => {
  it("restores an unconfirmed create and offers only its original payload for retry", async () => {
    const onSave = vi.fn();
    offlineStorageMock.value = {
      staffId: "staff",
      status: "unlocked",
      get: (id: string) =>
        Promise.resolve(
          id === "new-client-command"
            ? {
                schemaVersion: 1,
                commandId: "0192f0e0-1234-7abc-8def-123456789012",
                clientId: "0192f0e0-1234-7abc-8def-123456789013",
                draft: {
                  firstName: "Анна",
                  lastName: "Иванова",
                  middleName: null,
                  birthdate: null,
                  sourceId: "source",
                  contacts: [],
                },
              }
            : undefined,
        ),
      put: vi.fn().mockResolvedValue(undefined),
      remove: vi.fn().mockResolvedValue(undefined),
    };
    render(
      <ClientEditorDrawer
        open
        client={null}
        sourceOptions={sources}
        contactKinds={kinds}
        persistNewClientDraft
        onClose={vi.fn()}
        onSave={onSave}
      />,
    );

    const retry = await screen.findByRole("button", { name: "Повторить отправку" });
    expect(screen.getByLabelText("Имя")).toHaveProperty("value", "Анна");
    expect(
      screen.getByText("Предыдущая отправка не подтверждена. Повтор использует ту же команду."),
    ).not.toBeNull();
    fireEvent.click(retry);

    await waitFor(() =>
      expect(onSave).toHaveBeenCalledExactlyOnceWith({
        firstName: "Анна",
        lastName: "Иванова",
        middleName: null,
        birthdate: null,
        sourceId: "source",
        contacts: [],
      }),
    );
    offlineStorageMock.value = null;
  });

  it("keeps input typed while a saved draft is being restored", async () => {
    let resolveDraft: ((value: unknown) => void) | undefined;
    const put = vi.fn().mockResolvedValue(undefined);
    offlineStorageMock.value = {
      staffId: "staff",
      status: "unlocked",
      get: (id: string) =>
        id === "new-client-command"
          ? Promise.resolve(undefined)
          : new Promise((resolve) => (resolveDraft = resolve)),
      put,
      remove: vi.fn().mockResolvedValue(undefined),
    };
    render(
      <ClientEditorDrawer
        open
        client={null}
        sourceOptions={sources}
        contactKinds={kinds}
        persistNewClientDraft
        onClose={vi.fn()}
        onSave={vi.fn()}
      />,
    );

    const firstName = screen.getByRole("textbox", { name: /^Имя$/ });
    await waitFor(() => expect(firstName.matches(":disabled")).toBe(false));
    fireEvent.change(firstName, { target: { value: "Введено вручную" } });
    resolveDraft?.({
      schemaVersion: 1,
      firstName: "Из старого черновика",
      lastName: "Иванова",
      middleName: null,
      birthdate: null,
      sourceId: "source",
      contacts: [],
    });

    await waitFor(() => expect((firstName as HTMLInputElement).value).toBe("Введено вручную"));
    await waitFor(() =>
      expect(put).toHaveBeenCalledWith(
        "new-client-draft",
        expect.objectContaining({ firstName: "Введено вручную", schemaVersion: 1 }),
        "pending",
      ),
    );
    offlineStorageMock.value = null;
  });

  it("restores a later draft after a successful save starts a new editor session", async () => {
    const records = new Map<string, unknown>();
    const storage = {
      staffId: "staff",
      status: "unlocked",
      get: vi.fn(async (id: string) => records.get(id)),
      put: vi.fn(async (id: string, value: unknown) => {
        records.set(id, value);
      }),
      remove: vi.fn(async (id: string) => {
        records.delete(id);
      }),
    };
    offlineStorageMock.value = storage;
    const props = {
      client: null,
      sourceOptions: sources,
      contactKinds: kinds,
      persistNewClientDraft: true,
      onClose: vi.fn(),
      onSave: vi.fn(),
    };
    const { rerender } = render(<ClientEditorDrawer open {...props} />);

    const firstName = await screen.findByRole("textbox", { name: /^Имя$/ });
    await waitFor(() => expect(firstName.matches(":disabled")).toBe(false));
    fireEvent.change(firstName, { target: { value: "Новый черновик" } });
    await waitFor(() => expect(records.has("new-client-draft")).toBe(true));

    rerender(<ClientEditorDrawer open={false} draftSavedKey={1} {...props} />);
    rerender(<ClientEditorDrawer open draftSavedKey={1} {...props} />);

    await waitFor(() =>
      expect((screen.getByRole("textbox", { name: /^Имя$/ }) as HTMLInputElement).value).toBe(
        "Новый черновик",
      ),
    );
    offlineStorageMock.value = null;
  });

  it("clears the draft from the form when the vault locks", async () => {
    const put = vi.fn().mockResolvedValue(undefined);
    const storage = {
      staffId: "staff",
      status: "unlocked",
      get: vi.fn().mockResolvedValue(undefined),
      put,
      remove: vi.fn().mockResolvedValue(undefined),
    };
    offlineStorageMock.value = storage;
    const onSave = vi.fn();
    const { rerender } = render(
      <ClientEditorDrawer
        open
        client={null}
        sourceOptions={sources}
        contactKinds={kinds}
        persistNewClientDraft
        onClose={vi.fn()}
        onSave={onSave}
      />,
    );

    const firstName = await screen.findByRole("textbox", { name: /^Имя$/ });
    await waitFor(() => expect(firstName.matches(":disabled")).toBe(false));
    fireEvent.change(firstName, { target: { value: "Личные данные" } });
    await waitFor(() => expect(put).toHaveBeenCalled());

    offlineStorageMock.value = { ...storage, status: "blocked", put };
    rerender(
      <ClientEditorDrawer
        open
        client={null}
        sourceOptions={sources}
        contactKinds={kinds}
        persistNewClientDraft
        onClose={vi.fn()}
        onSave={onSave}
      />,
    );

    expect((screen.getByRole("textbox", { name: /^Имя$/ }) as HTMLInputElement).value).toBe("");
    expect(screen.getByRole("textbox", { name: /^Имя$/ }).matches(":disabled")).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));
    expect(onSave).not.toHaveBeenCalled();
    offlineStorageMock.value = null;
  });

  it("blocks invalid birthday input and saves only after it is corrected", () => {
    const onSave = vi.fn();
    render(
      <ClientEditorDrawer
        open
        client={{ ...client([]), birthdate: "2001-05-06" }}
        sourceOptions={sources}
        contactKinds={kinds}
        onClose={vi.fn()}
        onSave={onSave}
      />,
    );
    const birthday = screen.getByRole("textbox", { name: "Дата рождения" });

    fireEvent.change(birthday, { target: { value: "31.11.2026" } });
    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(onSave).not.toHaveBeenCalled();
    expect(birthday.getAttribute("aria-invalid")).toBe("true");
    expect(document.activeElement).toBe(birthday);

    fireEvent.change(birthday, { target: { value: "30.11.2026" } });
    fireEvent.blur(birthday);
    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(onSave).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({ birthdate: "2026-11-30" }),
    );
  });

  it("allows clearing an invalid birthday before saving", () => {
    const onSave = vi.fn();
    render(
      <ClientEditorDrawer
        open
        client={{ ...client([]), birthdate: "2001-05-06" }}
        sourceOptions={sources}
        contactKinds={kinds}
        onClose={vi.fn()}
        onSave={onSave}
      />,
    );
    fireEvent.change(screen.getByRole("textbox", { name: "Дата рождения" }), {
      target: { value: "31.11.2026" },
    });

    fireEvent.click(screen.getByRole("button", { name: "Дата рождения: очистить" }));
    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(onSave).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ birthdate: null }));
  });

  it("submits through the form and focuses an invalid required field", () => {
    const onSave = vi.fn();
    render(
      <ClientEditorDrawer
        open
        client={client([])}
        sourceOptions={sources}
        contactKinds={kinds}
        onClose={vi.fn()}
        onSave={onSave}
      />,
    );
    const name = screen.getByRole("textbox", { name: /^Имя$/ });
    fireEvent.change(name, { target: { value: " " } });

    fireEvent.submit(screen.getByRole("form", { name: "Изменить карточку" }));

    expect(onSave).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(name);
    expect(name.getAttribute("aria-invalid")).toBe("true");
    const errorId = name.getAttribute("aria-describedby");
    expect(errorId && document.getElementById(errorId)?.textContent).toContain(
      "Заполните фамилию, имя и источник клиента.",
    );

    fireEvent.change(name, { target: { value: "Анна" } });
    fireEvent.submit(screen.getByRole("form", { name: "Изменить карточку" }));

    expect(onSave).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ firstName: "Анна" }));
  });

  it("edits the birthday through the shared calendar and saves a calendar date", async () => {
    const onSave = vi.fn();
    render(
      <ClientEditorDrawer
        open
        client={{ ...client([]), birthdate: "2001-05-06" }}
        sourceOptions={sources}
        contactKinds={kinds}
        onClose={vi.fn()}
        onSave={onSave}
      />,
    );

    const birthday = screen.getByRole("textbox", { name: "Дата рождения" });
    expect((birthday as HTMLInputElement).value).toBe("06.05.2001");
    fireEvent.click(screen.getByRole("button", { name: "Дата рождения: открыть календарь" }));
    const calendar = await screen.findByRole("dialog", { name: "Календарь" });
    fireEvent.click(within(calendar).getByRole("button", { name: "понедельник, 7 мая 2001 г." }));
    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ birthdate: "2001-05-07" }));
  });

  it("requires a source before saving", () => {
    const onSave = vi.fn();
    render(
      <ClientEditorDrawer
        open
        client={null}
        sourceOptions={sources}
        contactKinds={kinds}
        onClose={vi.fn()}
        onSave={onSave}
      />,
    );

    fireEvent.change(screen.getByLabelText("Фамилия"), { target: { value: "Иванова" } });
    fireEvent.change(screen.getByLabelText("Имя"), { target: { value: "Анна" } });
    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(onSave).not.toHaveBeenCalled();
    expect(screen.getByText("Заполните фамилию, имя и источник клиента.").textContent).toBe(
      "Заполните фамилию, имя и источник клиента.",
    );
  });

  it("allows saving a client without contacts", async () => {
    const onSave = vi.fn();
    render(
      <ClientEditorDrawer
        open
        client={null}
        sourceOptions={sources}
        contactKinds={kinds}
        onClose={vi.fn()}
        onSave={onSave}
      />,
    );

    fireEvent.change(screen.getByLabelText("Фамилия"), { target: { value: "Иванова" } });
    fireEvent.change(screen.getByLabelText("Имя"), { target: { value: " Анна " } });
    fireEvent.click(screen.getByLabelText("Источник"));
    fireEvent.click(await screen.findByRole("option", { name: "Рекомендация" }));
    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(onSave).toHaveBeenCalledWith({
      firstName: "Анна",
      lastName: "Иванова",
      middleName: "",
      birthdate: null,
      sourceId: "source",
      contacts: [],
    });
  });

  it("requires an explicit replacement when removing a primary contact", () => {
    const onSave = vi.fn();
    render(
      <ClientEditorDrawer
        open
        client={client(phoneContacts)}
        sourceOptions={sources}
        contactKinds={kinds}
        onClose={vi.fn()}
        onSave={onSave}
      />,
    );

    const contact = screen.getByLabelText("Контакты");
    const removeContact = within(contact).getAllByRole("button", { name: "Удалить" })[0];
    if (!removeContact) throw new Error("Expected a contact removal button.");
    fireEvent.click(removeContact);
    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(onSave).not.toHaveBeenCalled();
    expect(
      screen.getByText("Для каждого вида контакта отметьте ровно один основной контакт."),
    ).not.toBeNull();
  });

  it("saves the explicitly selected remaining primary contact", () => {
    const onSave = vi.fn();
    render(
      <ClientEditorDrawer
        open
        client={client(phoneContacts)}
        sourceOptions={sources}
        contactKinds={kinds}
        onClose={vi.fn()}
        onSave={onSave}
      />,
    );

    const replacementPrimary = screen.getAllByRole("radio", { name: "Основной" })[1];
    if (!replacementPrimary) throw new Error("Expected a replacement primary contact.");
    fireEvent.click(replacementPrimary);
    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({
        contacts: [
          expect.objectContaining({ id: "phone-a", isPrimary: false }),
          expect.objectContaining({ id: "phone-b", isPrimary: true }),
        ],
      }),
    );
  });

  it("keeps a dirty draft when a server event refreshes the client", () => {
    const original = client(phoneContacts);
    const { rerender } = render(
      <ClientEditorDrawer
        open
        client={original}
        sourceOptions={sources}
        contactKinds={kinds}
        onClose={vi.fn()}
        onSave={vi.fn()}
      />,
    );
    fireEvent.change(screen.getByLabelText("Имя"), { target: { value: "Черновик" } });

    rerender(
      <ClientEditorDrawer
        open
        client={{ ...original, firstName: "Изменено на сервере" }}
        sourceOptions={sources}
        contactKinds={kinds}
        stale
        onClose={vi.fn()}
        onSave={vi.fn()}
      />,
    );

    expect((screen.getByLabelText("Имя") as HTMLInputElement).value).toBe("Черновик");
    expect(screen.getByText("Карточка изменилась на сервере. Ваш ввод сохранён.")).not.toBeNull();
  });
});
