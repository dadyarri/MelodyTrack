CREATE TABLE public.client_notes_history (
    id uuid PRIMARY KEY CHECK (id <> '00000000-0000-0000-0000-000000000000'::uuid),
    client_id uuid NOT NULL REFERENCES public.clients(id) ON DELETE RESTRICT,
    author_staff_id uuid NOT NULL CHECK (author_staff_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    changed_at timestamptz NOT NULL,
    protected_before text,
    protected_after text
);

CREATE INDEX ix_client_notes_history_client_time
    ON public.client_notes_history(client_id, changed_at, id);
