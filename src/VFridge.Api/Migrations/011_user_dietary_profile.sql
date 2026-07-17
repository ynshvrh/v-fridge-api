-- 011_user_dietary_profile.sql — Add dietary profile to users table.
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS dietary_profile VARCHAR(1000) NULL;
