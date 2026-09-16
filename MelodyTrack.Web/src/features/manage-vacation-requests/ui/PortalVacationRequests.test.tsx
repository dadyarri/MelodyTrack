import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { RpgTheme } from "@/shared/ui/rpg";

import { PortalVacationRequests } from "./PortalVacationRequests";

function renderForm(onCreate = vi.fn().mockResolvedValue(undefined)) {
  render(
    <RpgTheme>
      <PortalVacationRequests
        requests={[]}
        loading={false}
        error={null}
        mutationError={null}
        creating={false}
        cancelling={false}
        cancelled={false}
        onRetry={vi.fn()}
        onCreate={onCreate}
        onCancel={vi.fn()}
      />
    </RpgTheme>,
  );
  return onCreate;
}

describe("Portal vacation request form", () => {
  it("uses the Ant Design date and time range picker", () => {
    renderForm();

    expect(screen.getAllByLabelText("Период отпуска")[0]?.closest(".ant-picker")).toHaveClass("ant-picker");
  });

  it("requires a selected period", () => {
    const submit = renderForm();

    const form = screen.getByRole("button", { name: "Отправить заявку" }).closest("form");
    if (!form) throw new Error("Vacation request form is missing.");

    fireEvent.submit(form);

    expect(submit).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent("Окончание должно быть позже начала");
  });
});
