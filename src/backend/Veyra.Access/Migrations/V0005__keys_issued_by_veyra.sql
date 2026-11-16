-- Keys Veyra issues for its own agents (ADR 0019) have no administrator behind them.
ALTER TABLE access.ingestion_key ALTER COLUMN created_by DROP NOT NULL;
