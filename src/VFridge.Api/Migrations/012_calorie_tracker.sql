-- Add daily nutrition target columns to users table
ALTER TABLE users ADD COLUMN daily_calories_target INTEGER NULL;
ALTER TABLE users ADD COLUMN daily_protein_target NUMERIC(6, 2) NULL;
ALTER TABLE users ADD COLUMN daily_fat_target NUMERIC(6, 2) NULL;
ALTER TABLE users ADD COLUMN daily_carbs_target NUMERIC(6, 2) NULL;

-- Create nutrition logs table
CREATE TABLE nutrition_logs (
    id BIGSERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    date DATE NOT NULL,
    meal_type VARCHAR(32) NOT NULL, -- 'breakfast', 'lunch', 'dinner', 'snack'
    food_name VARCHAR(255) NOT NULL,
    quantity NUMERIC(10, 2) NULL,
    unit VARCHAR(20) NULL,
    calories INTEGER NOT NULL DEFAULT 0,
    protein NUMERIC(6, 2) NOT NULL DEFAULT 0.00,
    fat NUMERIC(6, 2) NOT NULL DEFAULT 0.00,
    carbs NUMERIC(6, 2) NOT NULL DEFAULT 0.00,
    logged_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Index for fast daily retrieval
CREATE INDEX ix_nutrition_logs_user_date ON nutrition_logs(user_id, date);
