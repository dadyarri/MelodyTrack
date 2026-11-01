CREATE TYPE public.contact_format AS ENUM ('phone', 'email', 'link');

CREATE TABLE public.client_sources (
    id uuid PRIMARY KEY CHECK (id <> '00000000-0000-0000-0000-000000000000'::uuid),
    name text NOT NULL CHECK (btrim(name) <> '')
);

CREATE TABLE public.contact_kinds (
    id uuid PRIMARY KEY CHECK (id <> '00000000-0000-0000-0000-000000000000'::uuid),
    name text NOT NULL CHECK (btrim(name) <> ''),
    format public.contact_format NOT NULL,
    is_archived boolean NOT NULL DEFAULT false
);

CREATE TABLE public.clients (
    id uuid PRIMARY KEY CHECK (id <> '00000000-0000-0000-0000-000000000000'::uuid),
    first_name text NOT NULL CHECK (btrim(first_name) <> ''),
    last_name text NOT NULL CHECK (btrim(last_name) <> ''),
    middle_name text,
    birthdate date,
    source_id uuid REFERENCES public.client_sources(id) ON DELETE RESTRICT,
    protected_notes text,
    created_at timestamptz NOT NULL,
    is_lead_closed boolean NOT NULL DEFAULT false,
    is_archived boolean NOT NULL DEFAULT false
);

CREATE INDEX ix_clients_source_id ON public.clients(source_id);

CREATE TABLE public.client_contacts (
    id uuid PRIMARY KEY CHECK (id <> '00000000-0000-0000-0000-000000000000'::uuid),
    client_id uuid NOT NULL REFERENCES public.clients(id) ON DELETE CASCADE,
    kind_id uuid NOT NULL REFERENCES public.contact_kinds(id) ON DELETE RESTRICT,
    protected_value text NOT NULL CHECK (btrim(protected_value) <> ''),
    protected_comment text,
    is_primary boolean NOT NULL DEFAULT false
);

CREATE INDEX ix_client_contacts_client_kind ON public.client_contacts(client_id, kind_id);
CREATE INDEX ix_client_contacts_kind_id ON public.client_contacts(kind_id);
CREATE UNIQUE INDEX ux_client_contacts_primary
    ON public.client_contacts(client_id, kind_id) WHERE is_primary;

-- Contact changes serialize on their owner before modifying any child row.
-- A row update also prevents write skew under repeatable-read isolation.
-- The original archive flag is preserved; there is no global client version.
CREATE FUNCTION public.lock_client_contact_owners() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    previous_owner uuid;
    next_owner uuid;
    owner_id uuid;
BEGIN
    IF TG_OP <> 'INSERT' THEN
        previous_owner := OLD.client_id;
    END IF;
    IF TG_OP <> 'DELETE' THEN
        next_owner := NEW.client_id;
    END IF;

    FOR owner_id IN
        SELECT DISTINCT candidate
        FROM (VALUES (previous_owner), (next_owner)) AS owners(candidate)
        WHERE candidate IS NOT NULL
        ORDER BY candidate
    LOOP
        UPDATE public.clients SET is_archived = is_archived WHERE id = owner_id;
    END LOOP;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER lock_client_contact_owners
    BEFORE INSERT OR UPDATE OR DELETE ON public.client_contacts
    FOR EACH ROW EXECUTE FUNCTION public.lock_client_contact_owners();

CREATE FUNCTION public.assert_client_contacts(owner_id uuid) RETURNS void
LANGUAGE plpgsql AS $$
BEGIN
    -- Deleting an aggregate removes its contacts in the same transaction.
    IF NOT EXISTS (SELECT 1 FROM public.clients WHERE id = owner_id) THEN
        RETURN;
    END IF;

    IF EXISTS (
        SELECT 1 FROM public.client_contacts
        WHERE client_id = owner_id
        GROUP BY kind_id
        HAVING count(*) FILTER (WHERE is_primary) <> 1
    ) THEN
        RAISE EXCEPTION USING
            ERRCODE = '23514',
            CONSTRAINT = 'ck_client_contacts_primary_required',
            MESSAGE = 'Each contact kind must have exactly one primary contact.';
    END IF;
END;
$$;

CREATE FUNCTION public.check_client_contacts() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP <> 'INSERT' THEN
        PERFORM public.assert_client_contacts(OLD.client_id);
    END IF;
    IF TG_OP <> 'DELETE' THEN
        PERFORM public.assert_client_contacts(NEW.client_id);
    END IF;
    RETURN NULL;
END;
$$;

-- Empty contact collections are valid. For each kind that is present, check
-- the final selection at commit, allowing gaps while changing the primary.
CREATE CONSTRAINT TRIGGER ck_client_contacts_primary_required
    AFTER INSERT OR UPDATE OR DELETE ON public.client_contacts
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION public.check_client_contacts();
