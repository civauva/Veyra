-- Agents in other networks (VEY-40). Such an agent exists from "Add an agent" on, and gets its key only when it is
-- installed with a one-time code.
ALTER TABLE agents.agent ALTER COLUMN ingestion_key_id DROP NOT NULL;

CREATE TABLE agents.enrollment (
    -- SHA-256 of the code. The code itself is shown once, on the Agents page.
    code_hash   bytea       PRIMARY KEY,
    agent_id    uuid        NOT NULL REFERENCES agents.agent (id),
    created_at  timestamptz NOT NULL,
    expires_at  timestamptz NOT NULL,
    -- Set when an installer used it. A code works once.
    used_at     timestamptz
);

CREATE INDEX enrollment_agent ON agents.enrollment (agent_id);
