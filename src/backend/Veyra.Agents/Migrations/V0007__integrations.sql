-- SQL Servers as managed integrations (VEY-78, ADR 0028): named instances, Windows authentication, modules with
-- intervals, template versions, and connection probes run through an agent.

ALTER TABLE agents.sql_server
    -- The named instance, reached through the SQL Server Browser; null when the server is reached by its port.
    ADD COLUMN instance         text,
    -- Veyra.Agents.SqlServers.SqlServerAuthentication: 1 SQL login, 2 Windows (NTLM), 3 integrated. A server that
    -- existed before this column signs in with a SQL login.
    ADD COLUMN authentication   smallint NOT NULL DEFAULT 1,
    -- The modules whose switch or interval differs from the default, as [{"id","enabled","intervalSeconds"}]. Empty
    -- for every server that existed before, which keeps reading exactly as it did.
    ADD COLUMN modules          jsonb    NOT NULL DEFAULT '[]',
    -- The version of integrations/sqlserver/collector.yaml the server is read with, and when and by whom it was set.
    -- A server that existed before was read with 0.1.0, the template before versions.
    ADD COLUMN template_version text     NOT NULL DEFAULT '0.1.0',
    ADD COLUMN template_set_at  timestamptz,
    -- The name of who set it, kept as it was; null for Veyra itself.
    ADD COLUMN template_set_by  text;

UPDATE agents.sql_server SET template_set_at = created_at;

ALTER TABLE agents.sql_server
    ALTER COLUMN template_set_at SET NOT NULL,
    ALTER COLUMN template_version DROP DEFAULT;

-- When the agent's Collector last started, from OpAMP health, for its uptime.
ALTER TABLE agents.agent ADD COLUMN health_started_at timestamptz;

-- A short-lived check of a SQL Server connection that an agent runs: Test connection before a server is saved, and
-- Validate now on a saved one. Its password lives only while it runs; its outcome is kept a day.
CREATE TABLE agents.sql_probe (
    id             uuid        PRIMARY KEY,
    -- The agent that runs it; null when there was none to run it.
    agent_id       uuid        REFERENCES agents.agent (id),
    -- The saved server it validates; null for a test before saving.
    server_id      uuid        REFERENCES agents.sql_server (id) ON DELETE CASCADE,
    platform       smallint    NOT NULL,
    address        text        NOT NULL,
    port           integer     NOT NULL,
    instance       text,
    authentication smallint    NOT NULL,
    login          text        NOT NULL,
    -- Encrypted with ASP.NET Core Data Protection, purpose Veyra.Agents.SqlServerPassword.v1; set to null as soon as
    -- the probe finishes.
    password       text,
    encryption     smallint    NOT NULL,
    database_name  text,
    started_at     timestamptz NOT NULL,
    finished_at    timestamptz,
    -- Veyra.Agents.Probes.ProbeState: 0 pending, 1 succeeded, 2 failed.
    state          smallint    NOT NULL,
    -- Veyra.Agents.Probes.ProbeFailure, for a failed one.
    reason         smallint,
    message        text,
    -- What the probe read, for a probe that succeeded.
    result         jsonb
);

-- The pending probes an agent's configuration holds.
CREATE INDEX sql_probe_pending ON agents.sql_probe (agent_id, started_at) WHERE state = 0;

-- A saved server's latest probe, for its page.
CREATE INDEX sql_probe_server ON agents.sql_probe (server_id, started_at DESC) WHERE server_id IS NOT NULL;
