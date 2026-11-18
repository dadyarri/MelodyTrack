import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { useForm } from "react-hook-form";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PaperPasswordInput } from "./PaperPasswordInput";

function PasswordForm({
  onSubmit,
  disabled = false,
}: {
  onSubmit: (values: { confirmation: string }) => void;
  disabled?: boolean;
}) {
  const { handleSubmit, register, watch } = useForm<{ confirmation: string }>({
    defaultValues: { confirmation: "" },
  });

  return (
    <form onSubmit={handleSubmit((values) => onSubmit(values))}>
      <label htmlFor="confirmation">Подтверждение</label>
      <PaperPasswordInput
        id="confirmation"
        toggleLabel="подтверждение пароля"
        disabled={disabled}
        {...register("confirmation")}
      />
      <output aria-label="Сохранённое значение">{watch("confirmation")}</output>
      <button type="submit">Отправить</button>
    </form>
  );
}

afterEach(cleanup);

describe("PaperPasswordInput", () => {
  it("toggles visibility without submitting and preserves the registered value", async () => {
    const onSubmit = vi.fn();
    const view = render(<PasswordForm onSubmit={onSubmit} />);
    const input = screen.getByLabelText("Подтверждение") as HTMLInputElement;
    fireEvent.change(input, { target: { value: "private-value" } });

    expect(input.type).toBe("password");
    expect(screen.getByLabelText("Сохранённое значение").textContent).toBe("private-value");
    fireEvent.click(screen.getByRole("button", { name: "Показать подтверждение пароля" }));

    expect(input.type).toBe("text");
    expect(
      screen
        .getByRole("button", { name: "Скрыть подтверждение пароля" })
        .getAttribute("aria-pressed"),
    ).toBe("true");
    expect(screen.getByLabelText("Сохранённое значение").textContent).toBe("private-value");
    expect(onSubmit).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Отправить" }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalledWith({ confirmation: "private-value" }));

    view.rerender(<PasswordForm onSubmit={onSubmit} disabled />);
    expect(
      (screen.getByRole("button", { name: "Скрыть подтверждение пароля" }) as HTMLButtonElement)
        .disabled,
    ).toBe(true);
  });
});
