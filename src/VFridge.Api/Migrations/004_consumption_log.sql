-- 004_consumption_log.sql — append-only history of products leaving the fridge.
-- Written when a product reaches quantity = 0 via PATCH, or is deleted via DELETE
-- (whether by the user, by the daily worker, or by the cascade FK on user delete).
-- Idempotent.

CREATE TABLE IF NOT EXISTS consumption_log (
    id              BIGSERIAL PRIMARY KEY,
    user_id         INTEGER     NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    product_name    VARCHAR(255) NOT NULL,
    quantity        NUMERIC(10, 2),
    unit            VARCHAR(20),
    category        VARCHAR(32)  NOT NULL DEFAULT 'other',
    status          VARCHAR(16)  NOT NULL,   -- 'consumed' | 'wasted' | 'expired'
    age_days        INTEGER,                 -- product_created_at → consumed_at, in whole days
    consumed_at     TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT consumption_log_status_check CHECK (status IN ('consumed', 'wasted', 'expired'))
);

CREATE INDEX IF NOT EXISTS ix_consumption_log_user_consumed_at
    ON consumption_log(user_id, consumed_at DESC);
CREATE INDEX IF NOT EXISTS ix_consumption_log_user_status
    ON consumption_log(user_id, status);
