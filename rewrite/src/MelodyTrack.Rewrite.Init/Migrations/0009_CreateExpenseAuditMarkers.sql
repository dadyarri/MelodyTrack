-- General audit exposes identifiers and operation type, never descriptions or correction reasons.
CREATE VIEW public.expense_audit_markers AS
SELECT command_id AS entry_id, expense_id, author_staff_id, occurred_at,
       CASE WHEN before_description IS NULL THEN 'expense.created'
            WHEN after_description IS NULL THEN 'expense.deleted'
            ELSE 'expense.corrected' END AS operation
FROM public.expense_changes;
