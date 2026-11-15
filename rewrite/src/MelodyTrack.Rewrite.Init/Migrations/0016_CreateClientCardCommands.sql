CREATE TABLE public.client_card_commands (
    command_id uuid PRIMARY KEY CHECK (command_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    author_staff_id uuid NOT NULL REFERENCES public.staff_access_state(staff_id) ON DELETE RESTRICT,
    client_id uuid NOT NULL REFERENCES public.clients(id) ON DELETE RESTRICT,
    operation text NOT NULL CHECK (operation IN ('create', 'update', 'restore')),
    protected_request_fingerprint text NOT NULL,
    has_duplicate_contacts boolean NOT NULL DEFAULT false,
    applied_at timestamptz NOT NULL
);

CREATE INDEX ix_client_card_commands_client_id ON public.client_card_commands(client_id, applied_at);
