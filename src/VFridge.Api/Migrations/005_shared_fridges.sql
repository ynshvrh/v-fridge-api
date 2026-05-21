-- 005_shared_fridges.sql — multi-user fridges with role-based membership.
-- Creates the three new tables, gives every existing user a personal fridge,
-- re-points existing products at their owner's personal fridge, and locks
-- products.fridge_id to NOT NULL. Idempotent.

CREATE TABLE IF NOT EXISTS fridges (
    id          SERIAL PRIMARY KEY,
    name        VARCHAR(80)  NOT NULL,
    owner_id    INTEGER      NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS ix_fridges_owner ON fridges(owner_id);

CREATE TABLE IF NOT EXISTS fridge_members (
    fridge_id   INTEGER     NOT NULL REFERENCES fridges(id) ON DELETE CASCADE,
    user_id     INTEGER     NOT NULL REFERENCES users(id)   ON DELETE CASCADE,
    role        VARCHAR(16) NOT NULL,
    joined_at   TIMESTAMP   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (fridge_id, user_id),
    CONSTRAINT fridge_members_role_check CHECK (role IN ('owner', 'member'))
);
CREATE INDEX IF NOT EXISTS ix_fridge_members_user ON fridge_members(user_id);

CREATE TABLE IF NOT EXISTS fridge_invites (
    id          SERIAL PRIMARY KEY,
    fridge_id   INTEGER     NOT NULL REFERENCES fridges(id) ON DELETE CASCADE,
    email       VARCHAR(255) NOT NULL,
    token_hash  TEXT        NOT NULL,
    expires_at  TIMESTAMP   NOT NULL,
    accepted_at TIMESTAMP,
    created_at  TIMESTAMP   NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS ix_fridge_invites_hash   ON fridge_invites(token_hash);
CREATE INDEX IF NOT EXISTS ix_fridge_invites_fridge ON fridge_invites(fridge_id);

-- Backfill: one personal fridge per user, plus an 'owner' membership row.
INSERT INTO fridges (name, owner_id)
SELECT u.username || E'\'s fridge', u.id
FROM users u
WHERE NOT EXISTS (SELECT 1 FROM fridges f WHERE f.owner_id = u.id);

INSERT INTO fridge_members (fridge_id, user_id, role)
SELECT f.id, f.owner_id, 'owner'
FROM fridges f
WHERE NOT EXISTS (
    SELECT 1 FROM fridge_members m
    WHERE m.fridge_id = f.id AND m.user_id = f.owner_id
);

ALTER TABLE products
    ADD COLUMN IF NOT EXISTS fridge_id INTEGER REFERENCES fridges(id) ON DELETE CASCADE;

UPDATE products p
SET fridge_id = (SELECT f.id FROM fridges f WHERE f.owner_id = p.owner_id LIMIT 1)
WHERE p.fridge_id IS NULL;

-- Lock once everything is backfilled. Wrapped in DO so re-running the migration is a no-op.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'products' AND column_name = 'fridge_id' AND is_nullable = 'YES'
    ) THEN
        ALTER TABLE products ALTER COLUMN fridge_id SET NOT NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_products_fridge ON products(fridge_id);
