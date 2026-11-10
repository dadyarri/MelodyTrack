CREATE TABLE public.staff_credentials (
    staff_id uuid PRIMARY KEY REFERENCES public.staff_access_state(staff_id) ON DELETE RESTRICT,
    login text NOT NULL CHECK (length(btrim(login)) BETWEEN 1 AND 256),
    normalized_login text NOT NULL UNIQUE,
    password_hash text,
    protected_authenticator text,
    second_factor_enabled boolean NOT NULL DEFAULT false,
    last_authenticator_step bigint NOT NULL DEFAULT -1,
    revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0)
);
CREATE TABLE public.staff_passkeys (
    credential_id text PRIMARY KEY,
    staff_id uuid NOT NULL REFERENCES public.staff_credentials(staff_id) ON DELETE RESTRICT,
    public_key bytea NOT NULL,
    signature_counter bigint NOT NULL CHECK (signature_counter BETWEEN 0 AND 4294967295),
    user_handle bytea NOT NULL
);
CREATE TABLE public.staff_credential_links (
    id uuid PRIMARY KEY,
    token_hash text NOT NULL UNIQUE CHECK (token_hash ~ '^[0-9A-F]{64}$'),
    role public.staff_access_role NOT NULL,
    staff_id uuid REFERENCES public.staff_access_state(staff_id) ON DELETE RESTRICT,
    expires_at timestamptz NOT NULL,
    consumed boolean NOT NULL DEFAULT false
);
CREATE TABLE public.staff_credential_challenges (
    token_hash text PRIMARY KEY CHECK (token_hash ~ '^[0-9A-F]{64}$'),
    purpose text NOT NULL,
    staff_id uuid,
    credential_revision bigint NOT NULL,
    payload text NOT NULL,
    expires_at timestamptz NOT NULL
);
ALTER TABLE public.staff_sessions
    ADD COLUMN refresh_hash text UNIQUE CHECK (refresh_hash IS NULL OR refresh_hash ~ '^[0-9A-F]{64}$'),
    ADD COLUMN refresh_expires_at timestamptz,
    ADD COLUMN factor_verified boolean NOT NULL DEFAULT false;
CREATE INDEX ix_staff_credential_links_staff ON public.staff_credential_links(staff_id);

-- Typed security facts only. Login, password, token, TOTP and WebAuthn payloads never enter this journal.
CREATE TABLE public.staff_security_events (
    id uuid PRIMARY KEY,
    author_staff_id uuid REFERENCES public.staff_access_state(staff_id) ON DELETE RESTRICT,
    target_staff_id uuid REFERENCES public.staff_access_state(staff_id) ON DELETE RESTRICT,
    operation text NOT NULL CHECK (operation IN (
        'InvitationCreated', 'RecoveryStarted', 'CredentialsRegistered', 'PasswordChanged', 'AuthenticatorEnabled',
        'AuthenticatorDisabled', 'PasskeyAdded', 'PasskeyRemoved', 'SecondFactorChanged', 'SessionStarted',
        'SessionRefreshed', 'SessionsRevoked', 'AccountAccessChanged')),
    session_id uuid,
    occurred_at timestamptz NOT NULL
);
