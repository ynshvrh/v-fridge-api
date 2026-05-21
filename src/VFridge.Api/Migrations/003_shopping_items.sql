-- 003_shopping_items.sql — shopping-list rows. One user owns many items.
-- Idempotent.

CREATE TABLE IF NOT EXISTS shopping_items (
    id          SERIAL PRIMARY KEY,
    user_id     INTEGER     NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name        VARCHAR(255) NOT NULL,
    quantity    NUMERIC(10, 2),
    unit        VARCHAR(20),
    category    VARCHAR(32)  NOT NULL DEFAULT 'other',
    checked     BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT shopping_items_category_check CHECK (category IN (
        'dairy', 'meat-fish', 'vegetables', 'fruits', 'bakery',
        'pantry', 'snacks', 'drinks', 'alcohol', 'sauces',
        'frozen', 'canned-prepared', 'other'
    ))
);

CREATE INDEX IF NOT EXISTS ix_shopping_items_user ON shopping_items(user_id);
CREATE INDEX IF NOT EXISTS ix_shopping_items_user_checked ON shopping_items(user_id, checked);
