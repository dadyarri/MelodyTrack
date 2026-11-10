-- The source's closed-lead fact becomes the shared archived work state.
-- Updating is_archived also invokes the installed credential-revocation trigger.
UPDATE public.clients
SET is_archived = true
WHERE is_lead_closed AND NOT is_archived;

ALTER TABLE public.clients DROP COLUMN is_lead_closed;
