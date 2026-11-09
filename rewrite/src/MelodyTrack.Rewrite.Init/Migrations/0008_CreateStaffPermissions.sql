CREATE TYPE public.staff_access_role AS ENUM ('superuser', 'administrator', 'teacher');
CREATE TYPE public.staff_permission AS ENUM (
    'clients_read', 'clients_write', 'client_notes_read', 'client_notes_write',
    'schedule_read', 'schedule_plan', 'appointment_results',
    'client_absences', 'own_absence_requests', 'staff_absence_approval', 'working_schedule_prepare', 'working_schedule_approval',
    'finance_read', 'payment_record', 'payment_correct', 'refund_remaining', 'refund_all',
    'expenses_read', 'expenses_record', 'expenses_correct', 'expenses_delete', 'expenses_export',
    'tasks_read', 'tasks_process', 'task_rules_manage', 'catalogues_manage', 'staff_manage', 'policies_manage',
    'training_read', 'training_progress'
);
CREATE TYPE public.permission_data_scope AS ENUM ('own', 'available');
CREATE TYPE public.permission_time_scope AS ENUM ('today', 'current_month', 'any');

-- Only trusted account provisioning writes identity state. No public registration or role supplied by a request body.
CREATE TABLE public.staff_access_state (
    staff_id uuid PRIMARY KEY CHECK (staff_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    role public.staff_access_role NOT NULL,
    is_blocked boolean NOT NULL DEFAULT false,
    is_archived boolean NOT NULL DEFAULT false
);

CREATE TABLE public.role_permission_policy (
    role public.staff_access_role NOT NULL CHECK (role <> 'superuser'),
    permission public.staff_permission NOT NULL,
    allowed boolean NOT NULL,
    data_scope public.permission_data_scope NOT NULL,
    time_scope public.permission_time_scope NOT NULL,
    PRIMARY KEY (role, permission),
    CHECK (permission <> 'policies_manage' OR NOT allowed)
);

CREATE TABLE public.staff_permission_override (
    staff_id uuid NOT NULL REFERENCES public.staff_access_state(staff_id) ON DELETE RESTRICT,
    permission public.staff_permission NOT NULL,
    allowed boolean NOT NULL,
    data_scope public.permission_data_scope NOT NULL,
    time_scope public.permission_time_scope NOT NULL,
    PRIMARY KEY (staff_id, permission),
    CHECK (permission <> 'policies_manage' OR NOT allowed)
);

CREATE TABLE public.staff_policy_commands (
    command_id uuid PRIMARY KEY CHECK (command_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    author_staff_id uuid NOT NULL REFERENCES public.staff_access_state(staff_id) ON DELETE RESTRICT,
    target_staff_id uuid REFERENCES public.staff_access_state(staff_id) ON DELETE RESTRICT,
    target_role public.staff_access_role CHECK (target_role <> 'superuser'),
    permission public.staff_permission NOT NULL,
    before_allowed boolean,
    before_data_scope public.permission_data_scope,
    before_time_scope public.permission_time_scope,
    after_allowed boolean,
    after_data_scope public.permission_data_scope,
    after_time_scope public.permission_time_scope,
    occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK ((target_staff_id IS NOT NULL) <> (target_role IS NOT NULL)),
    CHECK ((after_allowed IS NULL AND after_data_scope IS NULL AND after_time_scope IS NULL)
        OR (after_allowed IS NOT NULL AND after_data_scope IS NOT NULL AND after_time_scope IS NOT NULL))
);

INSERT INTO public.role_permission_policy(role, permission, allowed, data_scope, time_scope)
SELECT role, permission, false, 'available', 'any'
FROM unnest(ARRAY['administrator', 'teacher']::public.staff_access_role[]) role
CROSS JOIN unnest(enum_range(NULL::public.staff_permission)) permission;

UPDATE public.role_permission_policy SET allowed = true
WHERE permission IN ('client_notes_read', 'client_absences', 'own_absence_requests', 'training_read', 'training_progress');

UPDATE public.role_permission_policy SET data_scope = 'own'
WHERE permission IN ('own_absence_requests', 'training_read', 'training_progress');

UPDATE public.role_permission_policy SET allowed = true
WHERE role = 'administrator' AND permission IN (
    'clients_read', 'clients_write', 'client_notes_write', 'schedule_read', 'schedule_plan', 'appointment_results',
    'finance_read', 'payment_record', 'payment_correct', 'refund_remaining',
    'expenses_read', 'expenses_record', 'expenses_correct', 'tasks_read', 'tasks_process'
);

UPDATE public.role_permission_policy SET time_scope = 'today'
WHERE role = 'administrator' AND permission IN ('payment_record', 'payment_correct', 'expenses_read', 'expenses_record', 'expenses_correct');

UPDATE public.role_permission_policy SET time_scope = 'current_month'
WHERE role = 'administrator' AND permission = 'appointment_results';

UPDATE public.role_permission_policy SET allowed = true, data_scope = 'own'
WHERE role = 'teacher' AND permission IN ('clients_read', 'schedule_read', 'working_schedule_prepare');

-- This marker becomes visible atomically with protected notes history. No personal text enters the general journal projection.
CREATE VIEW public.client_notes_audit_markers AS
SELECT id AS entry_id, client_id, author_staff_id, changed_at AS occurred_at, 'client.notes.changed'::text AS operation
FROM public.client_notes_history;
