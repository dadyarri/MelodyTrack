import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { RpgButton, RpgProgress } from "./RpgPrimitives";

describe("RpgButton", () => {
  it("blocks repeated actions while pending and enables them after completion", () => {
    const onClick = vi.fn();
    const { rerender } = render(
      <RpgButton pending pendingLabel="Сохраняем…" onClick={onClick}>
        Сохранить
      </RpgButton>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Сохраняем…" }));

    expect(onClick).not.toHaveBeenCalled();
    expect(screen.getByRole("button")).toHaveAttribute("aria-busy", "true");

    rerender(<RpgButton onClick={onClick}>Сохранить</RpgButton>);
    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(onClick).toHaveBeenCalledOnce();
  });

  it("does not submit its surrounding form unless explicitly requested", () => {
    const onSubmit = vi.fn((event: React.SyntheticEvent<HTMLFormElement>) => {
      event.preventDefault();
    });
    render(
      <form onSubmit={onSubmit}>
        <RpgButton>Открыть</RpgButton>
        <RpgButton type="submit">Отправить</RpgButton>
      </form>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Открыть" }));

    expect(onSubmit).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Отправить" }));

    expect(onSubmit).toHaveBeenCalledOnce();
  });
});

describe("RpgProgress", () => {
  it.each([
    { value: -5, max: 10, expected: 0, expectedMax: 10 },
    { value: 15, max: 10, expected: 10, expectedMax: 10 },
    { value: Number.NaN, max: 0, expected: 0, expectedMax: 100 },
  ])("keeps invalid progress accessible for $value of $max", ({ value, max, expected, expectedMax }) => {
    render(<RpgProgress label="Изучено тем" value={value} max={max} />);

    expect(screen.getByRole("progressbar", { name: "Изучено тем" })).toHaveAttribute("value", String(expected));
    expect(screen.getByRole("progressbar")).toHaveAttribute("max", String(expectedMax));
  });
});
