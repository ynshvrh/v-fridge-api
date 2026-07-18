-- 013_user_avatar.sql — Add avatar field to users table.
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS avatar VARCHAR(50) NULL;
