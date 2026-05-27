-- 007_user_preferred_language.sql — store the user's preferred UI language.
-- Used by future server-initiated emails (verification, daily expiry digest from the worker)
-- and by the chat endpoint to pick a culturally appropriate system prompt for the chef.
-- Idempotent: re-running on a database that already has the column / constraint is a no-op.

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS preferred_language VARCHAR(8) NOT NULL DEFAULT 'en';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE table_name = 'users' AND constraint_name = 'users_preferred_language_check'
    ) THEN
        ALTER TABLE users
            ADD CONSTRAINT users_preferred_language_check
            CHECK (preferred_language IN ('en', 'uk'));
    END IF;
END $$;