export function isShortcutTarget(target: EventTarget | null) {
  const element = target as HTMLElement | null;
  if (!element) {
    return false;
  }

  if (element.isContentEditable) {
    return true;
  }

  const tagName = element.tagName;
  return tagName === "INPUT" || tagName === "TEXTAREA" || tagName === "SELECT";
}

function isLatinLetterShortcut(key: string) {
  return /^[a-z]$/i.test(key);
}

function getShortcutCode(key: string) {
  return `Key${key.toUpperCase()}`;
}

export function matchesPlainKey(event: KeyboardEvent, key: string) {
  if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
    return false;
  }

  if (isLatinLetterShortcut(key)) {
    return event.code === getShortcutCode(key) || event.key.toLowerCase() === key.toLowerCase();
  }

  return event.key.toLowerCase() === key.toLowerCase();
}

export function formatShortcutLabel(shortcut: string) {
  return isLatinLetterShortcut(shortcut) ? shortcut.toUpperCase() : shortcut;
}
