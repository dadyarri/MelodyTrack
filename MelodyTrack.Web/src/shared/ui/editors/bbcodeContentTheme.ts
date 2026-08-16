export const bbcodeContentClassName = "melodytrack-bbcode-content";
const bbcodeContentStyleId = "melodytrack-bbcode-content-theme";

export function getBbcodeContentCss(selector: string, { includeShell = true }: { includeShell?: boolean } = {}) {
  return `
    ${selector} {
      ${includeShell ? "color: var(--mt-bbcode-text); font-size: 15px; line-height: 1.6;" : ""}
    }

    ${selector} :where(p, ul, ol, blockquote, pre) {
      margin: 0 0 12px;
    }

    ${selector} :where(p, ul, ol, blockquote, pre):last-child {
      margin-bottom: 0;
    }

    ${selector} blockquote {
      border-left: 3px solid var(--mt-bbcode-border-strong);
      margin-inline: 0;
      padding-left: 10px;
      color: var(--mt-bbcode-muted);
    }

    ${selector} :where(code, pre) {
      font-family: "JetBrainsMono Nerd Font Mono", "JetBrains Mono", "Fira Code", monospace;
    }

    ${selector} pre {
      border-radius: 12px;
      background: var(--mt-bbcode-card);
      padding: 12px;
      white-space: pre-wrap;
    }

    ${selector} a {
      color: var(--mt-bbcode-accent);
    }

    ${selector} :where(ul, ol) {
      padding-left: 22px;
    }
  `;
}

export function ensureBbcodeContentTheme() {
  const existingStyle = document.getElementById(bbcodeContentStyleId);
  const style = existingStyle ?? document.createElement("style");

  style.id = bbcodeContentStyleId;
  style.textContent = `
    :root {
      --mt-bbcode-text: var(--text-main);
      --mt-bbcode-muted: var(--text-muted);
      --mt-bbcode-border-strong: var(--border-strong);
      --mt-bbcode-card: var(--bg-card);
      --mt-bbcode-accent: var(--accent);
    }

    ${getBbcodeContentCss(`.${bbcodeContentClassName}`)}
  `;

  if (!existingStyle) {
    document.head.append(style);
  }
}
