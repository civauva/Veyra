-- PostgreSQL servers as monitored systems (VEY-33, ADR 0018 and 0019), read by an agent like a SQL Server.
CREATE TABLE agents.postgres_server (
    id           uuid        PRIMARY KEY,
    -- The server's name in Veyra: its database block, server.address in its telemetry.
    name         text        NOT NULL UNIQUE,
    address      text        NOT NULL,
    port         integer     NOT NULL,
    environment  text        NOT NULL,
    agent_id     uuid        NOT NULL REFERENCES agents.agent (id),
    -- The role the agent signs in as, a member of pg_monitor.
    login        text        NOT NULL,
    -- Encrypted with ASP.NET Core Data Protection, purpose Veyra.Agents.PostgresServerPassword.v1. Never sent back.
    password     text        NOT NULL,
    -- Veyra.Agents.PostgresServers.PostgresEncryption: 1 verify-full, 2 require (trust the certificate), 3 disable.
    encryption   smallint    NOT NULL,
    created_at   timestamptz NOT NULL,
    changed_at   timestamptz NOT NULL
);

CREATE INDEX postgres_server_agent ON agents.postgres_server (agent_id);
