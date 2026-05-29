-- 010_meal_plans.sql — persist the latest meal plan per fridge so the planner
-- screen restores its state on revisit instead of hitting the LLM every time.
-- One row per fridge (UNIQUE(fridge_id)); regenerate is an UPSERT.
-- Idempotent: re-running on a database that already has the table is a no-op.

CREATE TABLE IF NOT EXISTS meal_plans (
    id              SERIAL       PRIMARY KEY,
    fridge_id       INTEGER      NOT NULL UNIQUE REFERENCES fridges(id) ON DELETE CASCADE,
    meals_json      TEXT         NOT NULL,
    gap_items_json  TEXT         NOT NULL,
    created_at      TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at      TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_meal_plans_updated_at ON meal_plans(updated_at);
