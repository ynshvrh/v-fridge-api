-- 008_user_cuisine_preference.sql — decouple culinary preference from UI language.
-- preferred_language now controls only the UI; cuisine_preference is what the chef
-- reads to bias recipe suggestions. Defaults to 'any' so existing users get a
-- neutral chef until they pick a cuisine in Settings.
-- Idempotent: re-running on a database that already has the column / constraint is a no-op.

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS cuisine_preference VARCHAR(32) NOT NULL DEFAULT 'any';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE table_name = 'users' AND constraint_name = 'users_cuisine_preference_check'
    ) THEN
        ALTER TABLE users
            ADD CONSTRAINT users_cuisine_preference_check
            CHECK (cuisine_preference IN (
                'ukrainian',
                'georgian',
                'italian',
                'french',
                'mexican',
                'middle-eastern',
                'indian',
                'chinese',
                'japanese',
                'thai',
                'american',
                'any'
            ));
    END IF;
END $$;
