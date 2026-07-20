-- 014_saved_recipes.sql — create saved_recipes table to persist user's favorite recipes
CREATE TABLE IF NOT EXISTS saved_recipes (
    id               SERIAL       PRIMARY KEY,
    user_id          INTEGER      NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    fridge_id        INTEGER      NULL REFERENCES fridges(id) ON DELETE SET NULL,
    name             VARCHAR(255) NOT NULL,
    description      TEXT         NULL,
    ingredients_json TEXT         NOT NULL DEFAULT '[]',
    steps_json       TEXT         NOT NULL DEFAULT '[]',
    calories         INTEGER      NOT NULL DEFAULT 0,
    protein          NUMERIC(6,2) NOT NULL DEFAULT 0,
    fat              NUMERIC(6,2) NOT NULL DEFAULT 0,
    carbs            NUMERIC(6,2) NOT NULL DEFAULT 0,
    created_at       TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_saved_recipes_user_id ON saved_recipes(user_id);
