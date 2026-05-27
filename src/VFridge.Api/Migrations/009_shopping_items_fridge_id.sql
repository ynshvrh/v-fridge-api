-- 009_shopping_items_fridge_id.sql — scope shopping items to a fridge instead of
-- a user. Mirrors the pattern from migration 005 that did the same for products:
-- add the column nullable, backfill to the user's first owned fridge, then enforce
-- NOT NULL.
-- Idempotent: re-running on a database that already has the column / constraint is a no-op.

ALTER TABLE shopping_items
    ADD COLUMN IF NOT EXISTS fridge_id INTEGER REFERENCES fridges(id) ON DELETE CASCADE;

UPDATE shopping_items si
SET fridge_id = (
    SELECT f.id
    FROM fridges f
    WHERE f.owner_id = si.user_id
    ORDER BY f.id
    LIMIT 1
)
WHERE si.fridge_id IS NULL;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'shopping_items' AND column_name = 'fridge_id' AND is_nullable = 'YES'
    ) THEN
        ALTER TABLE shopping_items ALTER COLUMN fridge_id SET NOT NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_shopping_items_fridge ON shopping_items(fridge_id);
