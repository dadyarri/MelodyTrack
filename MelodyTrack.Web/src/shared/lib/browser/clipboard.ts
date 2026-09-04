export async function copyTextToClipboard(value: string): Promise<boolean> {
  try {
    const clipboard = Reflect.get(navigator, "clipboard") as Clipboard | undefined;
    if (!clipboard || typeof clipboard.writeText !== "function") {
      return false;
    }

    await clipboard.writeText(value);
    return true;
  } catch {
    return false;
  }
}
