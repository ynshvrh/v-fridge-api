-- 006_username_display_name.sql — relax username from a unique handle to a display name.
-- Email is the only user-facing unique identifier from now on. Username collisions are
-- fine because we always show username + email side-by-side where it matters.
-- Idempotent: only drops the constraint if it still exists.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE table_name = 'users' AND constraint_name = 'users_username_key'
    ) THEN
        ALTER TABLE users DROP CONSTRAINT users_username_key;
    END IF;
END $$;
