CREATE TYPE public.employee_training_status AS ENUM ('not_started', 'in_progress', 'completed');

CREATE TABLE public.employee_training_progress (
    staff_id uuid NOT NULL CHECK (staff_id <> '00000000-0000-0000-0000-000000000000'),
    scenario_id text NOT NULL CHECK (btrim(scenario_id) <> ''),
    version integer NOT NULL CHECK (version > 0),
    completed_version integer CHECK (completed_version > 0 AND completed_version <= version),
    status public.employee_training_status NOT NULL,
    next_step integer NOT NULL CHECK (next_step >= 0),
    demo_state_schema_version integer NOT NULL CHECK (demo_state_schema_version = 1),
    demo_state jsonb NOT NULL CHECK (jsonb_typeof(demo_state) = 'object'),
    is_running boolean NOT NULL,
    was_deferred boolean NOT NULL,
    auto_start_skipped_for_open_form boolean NOT NULL,
    revision bigint NOT NULL CHECK (revision > 0),
    PRIMARY KEY (staff_id, scenario_id),
    CHECK (NOT is_running OR status = 'in_progress'),
    CHECK (status <> 'not_started' OR (next_step = 0 AND NOT is_running AND NOT was_deferred)),
    CHECK (status <> 'completed' OR (completed_version IS NOT NULL AND completed_version = version AND next_step > 0 AND NOT is_running))
);

CREATE TABLE public.employee_training_article_progress (
    staff_id uuid NOT NULL CHECK (staff_id <> '00000000-0000-0000-0000-000000000000'),
    article_id text NOT NULL CHECK (btrim(article_id) <> ''),
    version integer NOT NULL CHECK (version > 0),
    read_version integer CHECK (read_version > 0 AND read_version <= version),
    has_new_update boolean NOT NULL,
    revision bigint NOT NULL CHECK (revision > 0),
    PRIMARY KEY (staff_id, article_id),
    CHECK (read_version IS NULL OR read_version <> version OR NOT has_new_update)
);
