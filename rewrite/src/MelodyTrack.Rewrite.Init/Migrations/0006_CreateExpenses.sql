CREATE TABLE public.expense_categories (
    id uuid PRIMARY KEY CHECK (id <> '00000000-0000-0000-0000-000000000000'::uuid),
    name text NOT NULL CHECK (length(btrim(name)) > 0)
);

CREATE TABLE public.expenses (
    id uuid PRIMARY KEY CHECK (id <> '00000000-0000-0000-0000-000000000000'::uuid),
    description text NOT NULL CHECK (length(btrim(description)) > 0),
    amount numeric NOT NULL CHECK (amount >= 0 AND amount = trunc(amount, 2)),
    date date NOT NULL,
    category_id uuid REFERENCES public.expense_categories(id) ON DELETE RESTRICT,
    is_deleted boolean NOT NULL DEFAULT false
);

CREATE TABLE public.expense_changes (
    command_id uuid PRIMARY KEY CHECK (command_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    expense_id uuid NOT NULL REFERENCES public.expenses(id) ON DELETE RESTRICT,
    author_staff_id uuid NOT NULL CHECK (author_staff_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    occurred_at timestamptz NOT NULL,
    comment text,
    before_description text,
    before_amount numeric,
    before_date date,
    before_category_id uuid,
    after_description text,
    after_amount numeric,
    after_date date,
    after_category_id uuid,
    CHECK (before_description IS NOT NULL OR after_description IS NOT NULL),
    CHECK ((before_description IS NULL AND before_amount IS NULL AND before_date IS NULL AND before_category_id IS NULL)
        OR (before_description IS NOT NULL AND before_amount IS NOT NULL AND before_date IS NOT NULL)),
    CHECK ((after_description IS NULL AND after_amount IS NULL AND after_date IS NULL AND after_category_id IS NULL)
        OR (after_description IS NOT NULL AND after_amount IS NOT NULL AND after_date IS NOT NULL))
);

CREATE INDEX ix_expenses_date ON public.expenses(date, id) WHERE NOT is_deleted;
CREATE INDEX ix_expense_changes_expense ON public.expense_changes(expense_id, occurred_at, command_id);
