CREATE TABLE public.staff_sessions (
    id uuid PRIMARY KEY CHECK (id <> '00000000-0000-0000-0000-000000000000'::uuid),
    staff_id uuid NOT NULL REFERENCES public.staff_access_state(staff_id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    revoked_at timestamptz
);

-- Only the SHA-256 digest is persisted. Tokens carry identity through the session, never role or rights.
CREATE TABLE public.staff_access_tokens (
    token_hash text PRIMARY KEY CHECK (token_hash ~ '^[0-9A-F]{64}$'),
    session_id uuid NOT NULL REFERENCES public.staff_sessions(id) ON DELETE RESTRICT,
    issued_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    CHECK (expires_at = issued_at + INTERVAL '5 minutes')
);

CREATE INDEX ix_staff_sessions_staff ON public.staff_sessions(staff_id);
CREATE INDEX ix_staff_access_tokens_session ON public.staff_access_tokens(session_id);
