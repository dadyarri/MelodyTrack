import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("./BbcodeEditor", () => ({
  BbcodeEditor: ({ label, value }: { label: string; value?: string }) => (
    <div>
      {label}: {value}
    </div>
  ),
}));

import { LazyBbcodeEditor } from "./LazyBbcodeEditor";

describe("LazyBbcodeEditor", () => {
  it("loads the editor implementation and forwards its values", async () => {
    render(<LazyBbcodeEditor label="Материал" value="Урок" onChange={vi.fn()} />);

    expect(await screen.findByText("Материал: Урок")).toBeInTheDocument();
  });
});
