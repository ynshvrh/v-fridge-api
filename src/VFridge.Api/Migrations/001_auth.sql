-- 001_auth.sql — additive schema for the auth feature.
-- Idempotent: safe to re-run. Owned by the C# API; Drizzle does not touch these tables.

-- Tracks email verification state. A row means the address has been verified at `verified_at`.
CREATE TABLE IF NOT EXISTS email_verifications (
    user_id      INTEGER PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
    verified_at  TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- One-shot tokens emailed to users for email confirmation and password reset.
CREATE TABLE IF NOT EXISTS email_verification_tokens (
    id           SERIAL PRIMARY KEY,
    user_id      INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash   TEXT NOT NULL,
    expires_at   TIMESTAMP NOT NULL,
    used_at      TIMESTAMP,
    created_at   TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS ix_email_verification_tokens_hash ON email_verification_tokens(token_hash);
CREATE INDEX IF NOT EXISTS ix_email_verification_tokens_user ON email_verification_tokens(user_id);

-- Maps a user to one or more OAuth providers. Supports future providers besides Google.
CREATE TABLE IF NOT EXISTS oauth_logins (
    id                SERIAL PRIMARY KEY,
    user_id           INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    provider          VARCHAR(20) NOT NULL,
    provider_user_id  VARCHAR(255) NOT NULL,
    created_at        TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(provider, provider_user_id)
);
CREATE INDEX IF NOT EXISTS ix_oauth_logins_user ON oauth_logins(user_id);

-- Refresh tokens (stored hashed). Revoked or expired rows are kept for audit.
CREATE TABLE IF NOT EXISTS refresh_tokens (
    id           SERIAL PRIMARY KEY,
    user_id      INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash   TEXT NOT NULL UNIQUE,
    expires_at   TIMESTAMP NOT NULL,
    revoked_at   TIMESTAMP,
    created_at   TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_user ON refresh_tokens(user_id);
