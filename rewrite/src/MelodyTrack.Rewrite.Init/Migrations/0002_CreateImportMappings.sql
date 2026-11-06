CREATE TABLE public.import_id_map (
    source_table text NOT NULL CHECK (source_table IN ('ClientSources', 'Clients', 'ClientContacts')),
    source_id bytea NOT NULL CHECK (octet_length(source_id) = 16),
    part text NOT NULL DEFAULT '',
    target_id uuid NOT NULL UNIQUE CHECK (target_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    PRIMARY KEY (source_table, source_id, part)
);

CREATE TABLE public.import_contact_kinds (
    source_field text PRIMARY KEY CHECK (source_field IN ('Phone', 'Email', 'Telegram', 'Vk')),
    kind_id uuid NOT NULL UNIQUE REFERENCES public.contact_kinds(id) ON DELETE RESTRICT
);
