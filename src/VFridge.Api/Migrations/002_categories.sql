-- 002_categories.sql — adds a fixed-slug `category` column to products.
-- Idempotent: the column is added with a default, existing rows are backfilled to 'other'.

ALTER TABLE products
    ADD COLUMN IF NOT EXISTS category VARCHAR(32) NOT NULL DEFAULT 'other';

-- Lock the slug set at the DB layer so a bad client cannot stash arbitrary strings.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE table_name = 'products' AND constraint_name = 'products_category_check'
    ) THEN
        ALTER TABLE products ADD CONSTRAINT products_category_check
            CHECK (category IN (
                'dairy', 'meat-fish', 'vegetables', 'fruits', 'bakery',
                'pantry', 'snacks', 'drinks', 'alcohol', 'sauces',
                'frozen', 'canned-prepared', 'other'
            ));
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_products_owner_category ON products(owner_id, category);
