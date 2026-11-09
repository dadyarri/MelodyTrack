CREATE TYPE public.marketing_discount_kind AS ENUM ('percentage', 'fixed');
CREATE TYPE public.marketing_terms_kind AS ENUM ('payment', 'lessons', 'period', 'purchase');

CREATE TABLE public.marketing_campaigns (
    id uuid PRIMARY KEY CHECK (id <> '00000000-0000-0000-0000-000000000000'::uuid),
    code text COLLATE "C" NOT NULL CHECK (btrim(code) <> ''),
    activation_starts_at timestamptz NOT NULL,
    activation_ends_at timestamptz NOT NULL,
    discount_kind public.marketing_discount_kind NOT NULL,
    discount_value numeric NOT NULL,
    terms_kind public.marketing_terms_kind NOT NULL,
    lesson_count integer,
    period_ticks bigint,
    purchase_sizes integer[],
    returning_gap_ticks bigint CHECK (returning_gap_ticks > 0),
    is_stopped boolean NOT NULL,
    CONSTRAINT ux_marketing_campaigns_code UNIQUE(code),
    CHECK (activation_ends_at > activation_starts_at),
    CHECK ((discount_kind = 'percentage' AND discount_value > 0 AND discount_value <= 100)
        OR (discount_kind = 'fixed' AND discount_value >= 0 AND discount_value < 'Infinity'::numeric AND discount_value = round(discount_value, 2))),
    CHECK ((terms_kind = 'payment' AND lesson_count IS NULL AND period_ticks IS NULL AND purchase_sizes IS NULL)
        OR (terms_kind = 'lessons' AND lesson_count IS NOT NULL AND lesson_count > 0 AND period_ticks IS NULL AND purchase_sizes IS NULL)
        OR (terms_kind = 'period' AND lesson_count IS NULL AND period_ticks IS NOT NULL AND period_ticks > 0 AND purchase_sizes IS NULL)
        OR (terms_kind = 'purchase' AND lesson_count IS NULL AND period_ticks IS NULL AND purchase_sizes IS NOT NULL
            AND cardinality(purchase_sizes) > 0 AND purchase_sizes <@ ARRAY[4, 8] AND array_position(purchase_sizes, NULL) IS NULL))
);
