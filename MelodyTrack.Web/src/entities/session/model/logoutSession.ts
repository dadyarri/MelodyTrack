export async function logoutSession({ revoke, clear }: { revoke: () => Promise<void>; clear: () => void }) {
  try {
    await revoke();
  } finally {
    clear();
  }
}
