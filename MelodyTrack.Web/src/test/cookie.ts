export function setTestCookie(cookie: string): void {
  // biome-ignore lint/suspicious/noDocumentCookie: Tests must exercise the synchronous cookie API used by supported browsers.
  document.cookie = cookie;
}
