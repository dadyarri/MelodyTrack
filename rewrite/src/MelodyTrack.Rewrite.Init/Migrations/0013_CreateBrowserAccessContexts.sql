CREATE TABLE public.browser_access_contexts (
    id uuid PRIMARY KEY,
    secret_hash text NOT NULL UNIQUE CHECK (secret_hash ~ '^[0-9A-F]{64}$'),
    active_kind text CHECK (active_kind IN ('staff','client')),
    active_session_id uuid,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK ((active_kind IS NULL) = (active_session_id IS NULL))
);
ALTER TABLE public.staff_sessions
    ADD COLUMN browser_context_id uuid REFERENCES public.browser_access_contexts(id) ON DELETE RESTRICT;
ALTER TABLE public.client_access_sessions
    ADD COLUMN browser_context_id uuid REFERENCES public.browser_access_contexts(id) ON DELETE RESTRICT;
ALTER TABLE public.staff_credential_challenges
    ADD COLUMN browser_context_id uuid REFERENCES public.browser_access_contexts(id) ON DELETE RESTRICT;
CREATE INDEX ix_staff_sessions_browser ON public.staff_sessions(browser_context_id);
CREATE INDEX ix_client_access_sessions_browser ON public.client_access_sessions(browser_context_id);
