import { describe, expect, it, vi } from "vitest";
import { page } from "vitest/browser";
import { render } from "vitest-browser-react";

import { StructuredHomeworkEditor } from "./StructuredHomework";

const emptyDocument = JSON.stringify({
  schemaVersion: 1,
  document: { type: "doc", content: [{ type: "paragraph" }] },
});

describe("Structured course document editor", () => {
  it("reflects formatting at the current selection", async () => {
    const value = JSON.stringify({
      schemaVersion: 1,
      document: {
        type: "doc",
        content: [
          { type: "paragraph", content: [{ type: "text", marks: [{ type: "bold" }], text: "Жирный" }] },
          { type: "paragraph", content: [{ type: "text", text: "Обычный" }] },
        ],
      },
    });
    const screen = await render(<StructuredHomeworkEditor value={value} exercises={[]} onChange={() => {}} />);
    const boldButton = screen.getByRole("button", { name: "Жирный" });

    await screen.getByText("Жирный", { exact: true }).dblClick();

    await expect.element(boldButton).toHaveAttribute("aria-pressed", "true");

    await screen.getByText("Обычный", { exact: true }).dblClick();

    await expect.element(boldButton).toHaveAttribute("aria-pressed", "false");

    await boldButton.click();

    await expect.element(boldButton).toHaveAttribute("aria-pressed", "true");
  });

  it("offers a trainer type before inserting the node", async () => {
    const onChange = vi.fn();
    const screen = await render(<StructuredHomeworkEditor value={emptyDocument} exercises={[]} onChange={onChange} />);

    await screen.getByRole("button", { name: /Добавить тренажёр/ }).click();

    await expect.element(page.getByRole("menuitem", { name: "Гитарный бой" })).toBeVisible();
    await page.getByRole("menuitem", { name: "Гитарный бой" }).click();
    await expect.poll(() => onChange.mock.calls.length).toBeGreaterThan(0);
    expect(onChange.mock.lastCall?.[1]).toHaveLength(1);
    const exercise = document.querySelector<HTMLElement>("[data-node-view-wrapper]");
    expect(exercise).not.toBeNull();
    if (!exercise) throw new Error("Trainer node was not rendered");
    expect(exercise.getBoundingClientRect().width).toBeLessThanOrEqual(880);
  });

  it("edits inline link text and can disable the link", async () => {
    const value = JSON.stringify({
      schemaVersion: 1,
      document: {
        type: "doc",
        content: [{ type: "paragraph", content: [{ type: "text", text: "Открыть сайт" }] }],
      },
    });
    const onChange = vi.fn();
    const screen = await render(<StructuredHomeworkEditor value={value} exercises={[]} onChange={onChange} />);

    await screen.getByText("Открыть сайт", { exact: true }).dblClick();
    await screen.getByRole("button", { name: "Ссылка" }).click();
    await expect.element(page.getByRole("dialog", { name: "Настроить ссылку" })).toBeVisible();
    await page.getByRole("textbox", { name: "Текст ссылки" }).fill("Сайт проекта");
    await page.getByRole("textbox", { name: "Адрес ссылки" }).fill("https://example.com/course");
    await page.getByRole("button", { name: "Применить" }).click();
    await expect.poll(() => onChange.mock.calls.length).toBeGreaterThan(0);

    let serialized = JSON.parse(onChange.mock.lastCall?.[0] as string) as {
      document: { content: Array<{ content: Array<{ text: string; marks?: Array<{ type: string; attrs?: { href?: string } }> }> }> };
    };
    let linkedText = serialized.document.content[0]?.content.find((node) => node.text === "Сайт проекта");
    expect(linkedText?.text).toBe("Сайт проекта");
    expect(linkedText?.marks).toContainEqual(expect.objectContaining({ type: "link", attrs: expect.objectContaining({ href: "https://example.com/course" }) }));

    await screen.getByText("Сайт проекта", { exact: true }).dblClick();
    await screen.getByRole("button", { name: "Ссылка" }).click();
    await page.getByRole("switch", { name: "Ссылка включена" }).click();
    await page.getByRole("button", { name: "Применить" }).click();
    await expect.poll(() => onChange.mock.calls.length).toBeGreaterThan(1);

    serialized = JSON.parse(onChange.mock.lastCall?.[0] as string) as typeof serialized;
    linkedText = serialized.document.content[0]?.content.find((node) => node.text === "Сайт проекта");
    expect(linkedText?.text).toBe("Сайт проекта");
    expect(linkedText?.marks).toBeUndefined();
  });

  it("renders a labelled quote with a visible leading border", async () => {
    const value = JSON.stringify({
      schemaVersion: 1,
      document: {
        type: "doc",
        content: [{ type: "blockquote", content: [{ type: "paragraph", content: [{ type: "text", text: "Фраза" }] }] }],
      },
    });
    await render(<StructuredHomeworkEditor value={value} exercises={[]} onChange={() => {}} />);
    const quote = document.querySelector("blockquote");

    expect(quote).not.toBeNull();
    if (!quote) throw new Error("Quote was not rendered");
    expect(getComputedStyle(quote).borderInlineStartWidth).toBe("4px");
    expect(getComputedStyle(quote, "::before").content).toContain("Цитата");
  });
});
