import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { DirectoryClient } from "@/entities/client-directory";
import { ClientEditorDrawer } from "./ClientEditorDrawer";

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

  it("allows saving a client without contacts", () => {
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
    fireEvent.mouseDown(screen.getByLabelText("Источник"));
    fireEvent.click(screen.getByText("Рекомендация"));
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
