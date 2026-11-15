-- Levenshtein matching compares the unified client name in database queries.
-- No client data, identifiers or import mappings change in this step.
CREATE EXTENSION IF NOT EXISTS fuzzystrmatch WITH SCHEMA public;
