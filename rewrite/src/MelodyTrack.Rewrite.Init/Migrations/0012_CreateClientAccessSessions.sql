CREATE TABLE public.client_access_links (
    link_hash text PRIMARY KEY CHECK (link_hash ~ '^[0-9A-F]{64}$'),
    client_id uuid NOT NULL REFERENCES public.clients(id) ON DELETE RESTRICT,
    issued_by uuid NOT NULL REFERENCES public.staff_access_state(staff_id) ON DELETE RESTRICT,
    issued_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    revoked_at timestamptz
);
CREATE UNIQUE INDEX ux_client_access_links_active ON public.client_access_links(client_id) WHERE revoked_at IS NULL;

CREATE TABLE public.client_access_sessions (
    id uuid PRIMARY KEY CHECK (id <> '00000000-0000-0000-0000-000000000000'::uuid),
    link_hash text NOT NULL REFERENCES public.client_access_links(link_hash) ON DELETE RESTRICT,
    refresh_hash text UNIQUE NOT NULL CHECK (refresh_hash ~ '^[0-9A-F]{64}$'),
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    revoked_at timestamptz
);
CREATE INDEX ix_client_access_sessions_link ON public.client_access_sessions(link_hash);

CREATE TABLE public.client_access_tokens (
    token_hash text PRIMARY KEY CHECK (token_hash ~ '^[0-9A-F]{64}$'),
    session_id uuid NOT NULL REFERENCES public.client_access_sessions(id) ON DELETE RESTRICT,
    issued_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    CHECK (expires_at = issued_at + INTERVAL '5 minutes')
);
CREATE INDEX ix_client_access_tokens_session ON public.client_access_tokens(session_id);

-- A stable command receipt is the general audit marker. Neither secrets nor their hashes enter this projection.
CREATE TABLE public.client_access_commands (
    command_id uuid PRIMARY KEY CHECK (command_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    client_id uuid NOT NULL REFERENCES public.clients(id) ON DELETE RESTRICT,
    author_staff_id uuid NOT NULL REFERENCES public.staff_access_state(staff_id) ON DELETE RESTRICT,
    operation text NOT NULL CHECK (operation IN ('replace','revoke')),
    occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Archive permanently revokes links and remembered sessions in the same transaction.
-- Clearing is_archived cannot reactivate credentials that have already been revoked.
CREATE FUNCTION public.revoke_archived_client_access() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.is_archived AND NOT OLD.is_archived THEN
        UPDATE public.client_access_links SET revoked_at=COALESCE(revoked_at, clock_timestamp()) WHERE client_id=NEW.id;
        UPDATE public.client_access_sessions s SET revoked_at=COALESCE(s.revoked_at, clock_timestamp())
        FROM public.client_access_links l WHERE s.link_hash=l.link_hash AND l.client_id=NEW.id;
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER revoke_archived_client_access AFTER UPDATE OF is_archived ON public.clients
FOR EACH ROW EXECUTE FUNCTION public.revoke_archived_client_access();
