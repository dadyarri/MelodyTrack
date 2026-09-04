import "sceditor/minified/themes/default.min.css";
import "sceditor/minified/sceditor.min.js";
import "sceditor/minified/formats/bbcode.js";
import "sceditor/minified/icons/material.js";
import "sceditor/languages/ru.js";

import { Typography } from "antd";
import { useEffect, useEffectEvent, useId, useRef } from "react";

import { getBbcodeContentCss } from "./bbcodeContentTheme";
import styles from "./BbcodeEditor.module.css";

type EditorInstance = {
  bind: (eventName: string, handler: () => void) => void;
  closeDropDown: (focus?: boolean) => void;
  createDropDown: (caller: HTMLElement, name: string, content: HTMLElement) => void;
  destroy: () => void;
  execCommand: (command: string, param?: string) => void;
  focus: () => void;
  getSourceEditorValue: (filter?: boolean) => string;
  inSourceMode: () => boolean;
  currentNode: () => Node | null;
  setSourceEditorValue: (value: string) => void;
  sourceEditorCaret: {
    (): { end: number; start: number };
    (range: { end: number; start: number }): EditorInstance;
  };
  sourceEditorInsertText: (start: string, end?: string) => void;
  wysiwygEditorInsertHtml: (start: string, end?: string | null) => void;
  val: {
    (): string;
    (value: string): void;
  };
  melodyTrackCommitValue?: () => void;
};

type EditorCommandDefinition = {
  exec?: (this: EditorInstance, caller: HTMLElement) => void;
  state?: (this: EditorInstance) => number;
  txtExec?: (this: EditorInstance, caller: HTMLElement, selectedText: string) => void;
  tooltip?: string;
};

type EditorApi = {
  create: (
    element: HTMLTextAreaElement,
    options: {
      autoUpdate?: boolean;
      emoticonsEnabled?: boolean;
      format: string;
      height?: number;
      icons?: string;
      locale?: string;
      resizeEnabled?: boolean;
      toolbar?: string;
      width?: number | string;
    },
  ) => void;
  instance: (element: HTMLTextAreaElement) => EditorInstance | null;
  locale?: Record<string, Record<string, string>>;
  command?: {
    set: (name: string, definition: EditorCommandDefinition) => void;
  };
};

export type BbcodeEditorProps = {
  helper?: string;
  label: string;
  onChange: (value: string) => void;
  value?: string;
};

function getEditorApi() {
  const sceditor = (window as { sceditor?: EditorApi }).sceditor;

  if (!sceditor) {
    throw new Error("SCEditor is not available on window.");
  }

  return sceditor;
}

function escapeHtml(value: string) {
  return value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");
}

function getCurrentElement(editor: EditorInstance) {
  const currentNode = editor.currentNode();

  if (!currentNode) {
    return null;
  }

  return currentNode.nodeType === Node.ELEMENT_NODE ? (currentNode as Element) : currentNode.parentElement;
}

function unwrapElement(element: Element | null) {
  if (!element?.parentNode) {
    return;
  }

  const parent = element.parentNode;

  while (element.firstChild) {
    parent.insertBefore(element.firstChild, element);
  }

  parent.removeChild(element);
}

type SourceSelection = {
  caret: { end: number; start: number };
  selectedText: string;
  source: string;
};

type SourceTagRange = {
  closeEnd: number;
  closeStart: number;
  contentEnd: number;
  contentStart: number;
  openEnd: number;
  openStart: number;
};

function getSourceSelection(editor: EditorInstance): SourceSelection {
  const caret = editor.sourceEditorCaret();
  const source = editor.getSourceEditorValue(false);

  return {
    caret,
    source,
    selectedText: source.slice(caret.start, caret.end),
  };
}

function getLineRange(source: string, caret: { end: number; start: number }) {
  return {
    start: source.lastIndexOf("\n", Math.max(caret.start - 1, 0)) + 1,
    end: source.indexOf("\n", caret.end) === -1 ? source.length : source.indexOf("\n", caret.end),
  };
}

function normalizeSourceSelection(selection: SourceSelection, mode: "block" | "inline") {
  if (selection.caret.start !== selection.caret.end || mode === "inline") {
    return selection;
  }

  const lineRange = getLineRange(selection.source, selection.caret);

  return {
    ...selection,
    caret: lineRange,
    selectedText: selection.source.slice(lineRange.start, lineRange.end),
  };
}

function createSourceTags(tag: string, attr?: string) {
  return {
    openTag: attr ? `[${tag}=${attr}]` : `[${tag}]`,
    closeTag: `[/${tag}]`,
  };
}

function findContainingSourceTag(source: string, start: number, end: number, tag: string): SourceTagRange | null {
  const openTag = `[${tag}]`;
  const closeTag = `[/${tag}]`;

  for (let openStart = source.lastIndexOf(openTag, start); openStart >= 0; openStart = source.lastIndexOf(openTag, openStart - 1)) {
    const openEnd = openStart + openTag.length;
    const closeStart = source.indexOf(closeTag, openEnd);

    if (closeStart === -1) {
      continue;
    }

    const closeEnd = closeStart + closeTag.length;

    if (openEnd <= start && closeStart >= end) {
      return { openStart, openEnd, contentStart: openEnd, contentEnd: closeStart, closeStart, closeEnd };
    }
  }

  return null;
}

function replaceSourceRange(
  editor: EditorInstance,
  source: string,
  range: { end: number; start: number },
  replacement: string,
  nextSelection: { end: number; start: number },
) {
  editor.setSourceEditorValue(source.slice(0, range.start) + replacement + source.slice(range.end));
  editor.sourceEditorCaret(nextSelection);
  editor.melodyTrackCommitValue?.();
}

function toggleSourceBbcodeTag(editor: EditorInstance, tag: string, mode: "block" | "inline") {
  const selection = normalizeSourceSelection(getSourceSelection(editor), mode);
  const { openTag, closeTag } = createSourceTags(tag);
  const selectedTextHasTags = selection.selectedText.startsWith(openTag) && selection.selectedText.endsWith(closeTag);
  const hasSurroundingTags =
    selection.source.slice(selection.caret.start - openTag.length, selection.caret.start) === openTag &&
    selection.source.slice(selection.caret.end, selection.caret.end + closeTag.length) === closeTag;
  const containingTag = findContainingSourceTag(selection.source, selection.caret.start, selection.caret.end, tag);

  if (selectedTextHasTags) {
    const nextText = selection.selectedText.slice(openTag.length, selection.selectedText.length - closeTag.length);
    replaceSourceRange(editor, selection.source, selection.caret, nextText, {
      start: selection.caret.start,
      end: selection.caret.start + nextText.length,
    });
    return;
  }

  if (hasSurroundingTags) {
    const nextStart = selection.caret.start - openTag.length;
    replaceSourceRange(editor, selection.source, { start: nextStart, end: selection.caret.end + closeTag.length }, selection.selectedText, {
      start: nextStart,
      end: nextStart + selection.selectedText.length,
    });
    return;
  }

  if (containingTag) {
    const nextText = selection.source.slice(containingTag.contentStart, containingTag.contentEnd);
    const nextStart = containingTag.openStart;
    replaceSourceRange(editor, selection.source, { start: containingTag.openStart, end: containingTag.closeEnd }, nextText, {
      start: Math.max(selection.caret.start - openTag.length, nextStart),
      end: Math.max(selection.caret.end - openTag.length, nextStart),
    });
    return;
  }

  const wrappedText = `${openTag}${selection.selectedText}${closeTag}`;
  replaceSourceRange(editor, selection.source, selection.caret, wrappedText, {
    start: selection.caret.start + openTag.length,
    end: selection.caret.start + openTag.length + selection.selectedText.length,
  });
}

function toggleSourceList(editor: EditorInstance, ordered: boolean) {
  const selection = normalizeSourceSelection(getSourceSelection(editor), "block");
  const listTag = ordered ? "list=1" : "list";
  const containingList = findContainingSourceList(selection.source, selection.caret.start, selection.caret.end, ordered);

  if (containingList) {
    const innerText = selection.source.slice(containingList.contentStart, containingList.contentEnd);
    const unwrappedText = innerText
      .split("\n")
      .map((line) => line.replace(/^\s*\[\*\]\s?/, ""))
      .join("\n")
      .replace(/^\n|\n$/g, "");

    replaceSourceRange(editor, selection.source, { start: containingList.openStart, end: containingList.closeEnd }, unwrappedText, {
      start: containingList.openStart,
      end: containingList.openStart + unwrappedText.length,
    });
    return;
  }

  const lines = selection.selectedText.split("\n").filter((line) => line.length > 0);
  const listItems = lines.length > 0 ? lines.map((line) => `[*]${line}`).join("\n") : "[*]";
  const wrappedText = `[${listTag}]\n${listItems}\n[/list]`;

  replaceSourceRange(editor, selection.source, selection.caret, wrappedText, {
    start: selection.caret.start + `[${listTag}]\n`.length,
    end: selection.caret.start + `[${listTag}]\n`.length + listItems.length,
  });
}

function findContainingSourceList(source: string, start: number, end: number, ordered: boolean): SourceTagRange | null {
  const openTag = ordered ? "[list=1]" : "[list]";
  const closeTag = "[/list]";

  for (let openStart = source.lastIndexOf(openTag, start); openStart >= 0; openStart = source.lastIndexOf(openTag, openStart - 1)) {
    const openEnd = openStart + openTag.length;
    const closeStart = source.indexOf(closeTag, openEnd);

    if (closeStart === -1) {
      continue;
    }

    const closeEnd = closeStart + closeTag.length;

    if (openEnd <= start && closeStart >= end) {
      return { openStart, openEnd, contentStart: openEnd, contentEnd: closeStart, closeStart, closeEnd };
    }
  }

  return null;
}

function isSourceTagActive(editor: EditorInstance, tag: string, mode: "block" | "inline") {
  const selection = normalizeSourceSelection(getSourceSelection(editor), mode);
  return findContainingSourceTag(selection.source, selection.caret.start, selection.caret.end, tag) ? 1 : 0;
}

function isSourceListActive(editor: EditorInstance, ordered: boolean) {
  const selection = normalizeSourceSelection(getSourceSelection(editor), "block");
  return findContainingSourceList(selection.source, selection.caret.start, selection.caret.end, ordered) ? 1 : 0;
}

function isWysiwygTagActive(editor: EditorInstance, selector: string) {
  const currentElement = getCurrentElement(editor);
  return currentElement?.closest(selector) ? 1 : 0;
}

function isEditorInCodeContext(editor: EditorInstance) {
  if (!editor.inSourceMode()) {
    return Boolean(getCurrentElement(editor)?.closest("pre,code"));
  }

  const selection = getSourceSelection(editor);
  return Boolean(findContainingSourceTag(selection.source, selection.caret.start, selection.caret.end, "code"));
}

function setSourceToggleCommand(
  editorApi: EditorApi,
  commandName: string,
  tag: string,
  selector: string,
  mode: "block" | "inline",
  { disabledInCode = true }: { disabledInCode?: boolean } = {},
) {
  editorApi.command?.set(commandName, {
    txtExec(this: EditorInstance) {
      if (disabledInCode && isEditorInCodeContext(this)) {
        return;
      }

      toggleSourceBbcodeTag(this, tag, mode);
    },
    state(this: EditorInstance) {
      if (disabledInCode && isEditorInCodeContext(this)) {
        return -1;
      }

      return this.inSourceMode() ? isSourceTagActive(this, tag, mode) : isWysiwygTagActive(this, selector);
    },
  });
}

function setSourceListCommand(editorApi: EditorApi, commandName: string, ordered: boolean) {
  editorApi.command?.set(commandName, {
    txtExec(this: EditorInstance) {
      if (isEditorInCodeContext(this)) {
        return;
      }

      toggleSourceList(this, ordered);
    },
    state(this: EditorInstance) {
      if (isEditorInCodeContext(this)) {
        return -1;
      }

      return this.inSourceMode() ? isSourceListActive(this, ordered) : isWysiwygTagActive(this, ordered ? "ol" : "ul");
    },
  });
}

export function BbcodeEditor({ helper, label, onChange, value }: BbcodeEditorProps) {
  const textareaId = useId();
  const textareaRef = useRef<HTMLTextAreaElement | null>(null);
  const editorShellRef = useRef<HTMLDivElement | null>(null);
  const editorRef = useRef<EditorInstance | null>(null);
  const initialValueRef = useRef(value ?? "");
  const onChangeRef = useRef(onChange);
  const lastEditorValueRef = useRef(value ?? "");

  useEffect(() => {
    const editorApi = getEditorApi();
    const ruLocale = editorApi.locale?.ru;

    if (!ruLocale) {
      return;
    }

    ruLocale["URL:"] = "Ссылка:";
    ruLocale["Description (optional):"] = "Текст ссылки (необязательно):";
    ruLocale.Insert = "Вставить";
    ruLocale["Insert a link"] = "Вставить ссылку";
    ruLocale["Remove Formatting"] = "Очистить форматирование";

    setSourceToggleCommand(editorApi, "bold", "b", "b,strong", "inline");
    setSourceToggleCommand(editorApi, "italic", "i", "i,em", "inline");
    setSourceToggleCommand(editorApi, "underline", "u", "u", "inline");
    setSourceToggleCommand(editorApi, "strike", "s", "s,strike", "inline");
    setSourceToggleCommand(editorApi, "quote", "quote", "blockquote", "block");
    setSourceToggleCommand(editorApi, "code", "code", "pre,code", "block", { disabledInCode: false });
    setSourceListCommand(editorApi, "bulletlist", false);
    setSourceListCommand(editorApi, "orderedlist", true);

    editorApi.command?.set("removeformat", {
      exec(this: EditorInstance) {
        if (isEditorInCodeContext(this)) {
          return;
        }

        this.execCommand("removeformat");

        if (this.inSourceMode()) {
          return;
        }

        const currentElement = getCurrentElement(this);

        unwrapElement(currentElement?.closest("blockquote") ?? null);
        unwrapElement(currentElement?.closest("pre") ?? null);
        unwrapElement(currentElement?.closest("code") ?? null);
      },
      state(this: EditorInstance) {
        return isEditorInCodeContext(this) ? -1 : 0;
      },
      tooltip: "Remove Formatting",
    });

    editorApi.command?.set("link", {
      exec(this: EditorInstance, caller: HTMLElement) {
        if (isEditorInCodeContext(this)) {
          return;
        }

        const content = document.createElement("div");
        const urlLabel = document.createElement("label");
        const urlInput = document.createElement("input");
        const descLabel = document.createElement("label");
        const descInput = document.createElement("input");
        const submitButton = document.createElement("button");

        urlLabel.textContent = "Ссылка:";
        urlInput.type = "text";
        descLabel.textContent = "Текст ссылки (необязательно):";
        descInput.type = "text";
        submitButton.type = "button";
        submitButton.className = "button";
        submitButton.textContent = "Вставить";

        content.append(urlLabel, urlInput, descLabel, descInput, submitButton);

        const insertLink = (event: Event) => {
          const url = urlInput.value.trim();
          const text = descInput.value.trim() || url;

          if (!url) {
            this.closeDropDown(true);
            event.preventDefault();
            return;
          }

          this.focus();

          if (this.inSourceMode()) {
            this.sourceEditorInsertText(`[url=${url}]${text}[/url]`);
            this.melodyTrackCommitValue?.();
          } else {
            this.wysiwygEditorInsertHtml(`<a href="${escapeHtml(url)}">${escapeHtml(text)}</a>`);
          }

          this.closeDropDown(true);
          event.preventDefault();
        };

        submitButton.addEventListener("click", insertLink);
        content.addEventListener("keydown", (event) => {
          if (event.key === "Enter") {
            insertLink(event);
          }
        });

        this.createDropDown(caller, "insertlink", content);
        queueMicrotask(() => {
          urlInput.focus();
        });
      },
      state(this: EditorInstance) {
        return isEditorInCodeContext(this) ? -1 : 0;
      },
      tooltip: "Insert a link",
    });

    editorApi.command?.set("unlink", {
      state(this: EditorInstance) {
        return isEditorInCodeContext(this) ? -1 : 0;
      },
    });
  }, []);

  const syncContentTheme = useEffectEvent(() => {
    const shell = editorShellRef.current;
    const iframe = shell?.querySelector("iframe");

    if (!iframe?.contentDocument) {
      return;
    }

    const rootStyles = getComputedStyle(document.documentElement);
    const contentDocument = iframe.contentDocument;
    const themeStyleId = "melodytrack-bbcode-theme";
    const existingStyle = contentDocument.getElementById(themeStyleId);
    const themeStyle = existingStyle ?? contentDocument.createElement("style");

    themeStyle.id = themeStyleId;
    themeStyle.textContent = `
      :root {
        --mt-bbcode-bg: ${rootStyles.getPropertyValue("--bg-elevated").trim()};
        --mt-bbcode-text: ${rootStyles.getPropertyValue("--text-main").trim()};
        --mt-bbcode-muted: ${rootStyles.getPropertyValue("--text-muted").trim()};
        --mt-bbcode-border-strong: ${rootStyles.getPropertyValue("--border-strong").trim()};
        --mt-bbcode-card: ${rootStyles.getPropertyValue("--bg-card").trim()};
        --mt-bbcode-accent: ${rootStyles.getPropertyValue("--accent").trim()};
      }

      html,
      body {
        margin: 0;
        padding: 0;
        background: var(--mt-bbcode-bg);
        color: var(--mt-bbcode-text);
      }

      body {
        font:
          400 15px / 1.6 "Segoe UI",
          "Noto Sans",
          sans-serif;
        padding: 14px 16px;
      }

      ${getBbcodeContentCss("body", { includeShell: false })}
    `;

    if (!existingStyle) {
      contentDocument.head.append(themeStyle);
    }
  });

  useEffect(() => {
    onChangeRef.current = onChange;
  }, [onChange]);

  useEffect(() => {
    const textarea = textareaRef.current;

    if (!textarea || editorRef.current) {
      return;
    }

    const editorApi = getEditorApi();

    editorApi.create(textarea, {
      autoUpdate: true,
      emoticonsEnabled: false,
      format: "bbcode",
      height: 240,
      icons: "material",
      locale: "ru",
      resizeEnabled: false,
      toolbar: "bold,italic,underline,strike,removeformat|bulletlist,orderedlist|quote,code|link,unlink|source",
      width: "100%",
    });

    const editor = editorApi.instance(textarea);

    if (!editor) {
      return;
    }

    editorRef.current = editor;
    lastEditorValueRef.current = initialValueRef.current;
    editor.val(lastEditorValueRef.current);

    const syncValue = () => {
      const nextValue = editor.inSourceMode() ? editor.getSourceEditorValue(false) : editor.val();
      lastEditorValueRef.current = nextValue;
      onChangeRef.current(nextValue);
    };

    editor.melodyTrackCommitValue = syncValue;
    editor.bind("valuechanged", syncValue);
    editor.bind("blur", syncValue);
    syncContentTheme();

    const iframe = editorShellRef.current?.querySelector("iframe");
    iframe?.addEventListener("load", syncContentTheme);

    const themeObserver = new MutationObserver(() => {
      syncContentTheme();
    });
    themeObserver.observe(document.documentElement, {
      attributeFilter: ["data-theme", "style"],
      attributes: true,
    });

    return () => {
      themeObserver.disconnect();
      iframe?.removeEventListener("load", syncContentTheme);
      editor.melodyTrackCommitValue = undefined;
      editor.destroy();
      editorRef.current = null;
    };
  }, []);

  useEffect(() => {
    const editor = editorRef.current;
    const normalizedValue = value ?? "";

    if (!editor || lastEditorValueRef.current === normalizedValue) {
      return;
    }

    lastEditorValueRef.current = normalizedValue;
    editor.val(normalizedValue);
  }, [value]);

  return (
    <div className={styles.root}>
      <div className={styles.header}>
        <Typography.Text strong>{label}</Typography.Text>
        <Typography.Text type="secondary">BBCode</Typography.Text>
      </div>
      <div className={styles.editorShell} ref={editorShellRef}>
        <textarea id={textareaId} ref={textareaRef} defaultValue={value ?? ""} />
      </div>
      {helper ? <span className={styles.helper}>{helper}</span> : null}
    </div>
  );
}
