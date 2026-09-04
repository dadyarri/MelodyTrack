import { type ReactNode, useEffect, useMemo } from "react";

import styles from "./BbcodeContent.module.css";
import { bbcodeContentClassName, ensureBbcodeContentTheme } from "./bbcodeContentTheme";

type BbcodeContentProps = {
  value: string;
};

type BbcodeNode = string | { attr?: string; children: BbcodeNode[]; tag: string };

export function BbcodeContent({ value }: BbcodeContentProps) {
  useEffect(() => {
    ensureBbcodeContentTheme();
  }, []);

  const content = useMemo(() => parseBbcode(value).map((node, index) => renderBbcodeNode(node, `bbcode-${String(index)}`)), [value]);

  return <div className={`${styles.root} ${bbcodeContentClassName}`}>{content}</div>;
}

function parseBbcode(value: string): BbcodeNode[] {
  const root: Extract<BbcodeNode, { tag: string }> = { tag: "root", children: [] };
  const stack = [root];
  const tokenPattern = /\[(\/?)(b|i|u|s|quote|code|url|list|\*)(?:=([^\]]+))?\]/gi;
  let cursor = 0;
  let match = tokenPattern.exec(value);

  while (match != null) {
    if (match.index > cursor) {
      stack.at(-1)?.children.push(value.slice(cursor, match.index));
    }

    const closing = match[1] === "/";
    const tag = match[2].toLowerCase() === "*" ? "li" : match[2].toLowerCase();

    if (closing) {
      closeBbcodeTag(stack, tag);
    } else if (tag === "li") {
      closeBbcodeTag(stack, "li");
      openBbcodeTag(stack, "li", match[3]);
    } else {
      openBbcodeTag(stack, tag, match[3]);
    }

    cursor = tokenPattern.lastIndex;
    match = tokenPattern.exec(value);
  }

  if (cursor < value.length) {
    stack.at(-1)?.children.push(value.slice(cursor));
  }

  return root.children;
}

function openBbcodeTag(stack: Array<Extract<BbcodeNode, { tag: string }>>, tag: string, attr?: string) {
  const node = { attr, tag, children: [] };
  stack.at(-1)?.children.push(node);
  stack.push(node);
}

function closeBbcodeTag(stack: Array<Extract<BbcodeNode, { tag: string }>>, tag: string) {
  if (tag === "list" && stack.at(-1)?.tag === "li") {
    stack.pop();
  }

  while (stack.length > 1) {
    const current = stack.pop();
    if (current?.tag === tag) {
      return;
    }
  }
}

function renderBbcodeNode(node: BbcodeNode, key: string): ReactNode {
  if (typeof node === "string") {
    return renderBbcodeText(node, key);
  }

  const children = node.children.map((child, index) => renderBbcodeNode(child, `${key}-${String(index)}`));

  switch (node.tag) {
    case "b":
      return <strong key={key}>{children}</strong>;
    case "code":
      return (
        <pre key={key}>
          <code>{collectBbcodeText(node.children)}</code>
        </pre>
      );
    case "i":
      return <em key={key}>{children}</em>;
    case "li":
      return <li key={key}>{children}</li>;
    case "list":
      return node.attr === "1" ? <ol key={key}>{renderListChildren(node, key)}</ol> : <ul key={key}>{renderListChildren(node, key)}</ul>;
    case "quote":
      return <blockquote key={key}>{children}</blockquote>;
    case "s":
      return <s key={key}>{children}</s>;
    case "u":
      return <u key={key}>{children}</u>;
    case "url": {
      const href = node.attr ?? collectBbcodeText(node.children);
      const safeHref = getSafeHref(href);

      return safeHref ? (
        <a key={key} href={safeHref} target="_blank" rel="noreferrer">
          {children}
        </a>
      ) : (
        <span key={key}>{children}</span>
      );
    }
    default:
      return <span key={key}>{children}</span>;
  }
}

function renderListChildren(node: Extract<BbcodeNode, { tag: string }>, key: string) {
  const listItems = node.children.filter(
    (child): child is Extract<BbcodeNode, { tag: string }> => typeof child !== "string" && child.tag === "li",
  );

  if (listItems.length > 0) {
    return listItems.map((child, index) => renderBbcodeNode(child, `${key}-item-${String(index)}`));
  }

  return <li>{node.children.map((child, index) => renderBbcodeNode(child, `${key}-fallback-${String(index)}`))}</li>;
}

function renderBbcodeText(value: string, key: string) {
  return value.split("\n").flatMap((part, index) => {
    const textKey = `${key}-text-${String(index)}`;

    if (index === 0) {
      return part;
    }

    return [<br key={`${textKey}-br`} />, part];
  });
}

function collectBbcodeText(nodes: BbcodeNode[]): string {
  return nodes.map((node) => (typeof node === "string" ? node : collectBbcodeText(node.children))).join("");
}

function getSafeHref(href: string) {
  const trimmedHref = href.trim();

  if (/^(https?:|mailto:|tel:|\/|#)/i.test(trimmedHref)) {
    return trimmedHref;
  }

  return "";
}
