-- 015_consumption_log_fridge_id.sql — scope consumption log entries to a fridge
-- so shared fridges have accurate joint analytics.

ALTER TABLE consumption_log
    ADD COLUMN IF NOT EXISTS fridge_id INTEGER REFERENCES fridges(id) ON DELETE CASCADE;

UPDATE consumption_log cl
SET fridge_id = (
    SELECT f.id
    FROM fridges f
    WHERE f.owner_id = cl.user_id
    ORDER BY f.id
    LIMIT 1
)
WHERE cl.fridge_id IS NULL;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'consumption_log' AND column_name = 'fridge_id' AND is_nullable = 'YES'
    ) THEN
        ALTER TABLE consumption_log ALTER COLUMN fridge_id SET NOT NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_consumption_log_fridge ON consumption_log(fridge_id);
CREATE INDEX IF NOT EXISTS ix_consumption_log_fridge_consumed_at ON consumption_log(fridge_id, consumed_at);
CREATE INDEX IF NOT EXISTS ix_consumption_log_fridge_status ON consumption_log(fridge_id, status);
