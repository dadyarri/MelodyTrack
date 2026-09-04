export const authQueryKeys = {
  me: ["auth", "me"] as const,
  sessions: ["auth", "sessions"] as const,
  invite: (code?: string | null) => ["auth", "invite", code ?? null] as const,
};
