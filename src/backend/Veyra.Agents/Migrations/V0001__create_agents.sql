-- Veyra's agents and the systems they read (ADR 0019).

CREATE TABLE agents.agent (
    id                uuid        PRIMARY KEY,
    name              text        NOT NULL,
    -- Veyra.Agents.Agents.AgentPlacement: 1 built in, beside Veyra in Docker Compose; 2 in another network.
    placement         smallint    NOT NULL,
    -- The agent's ingestion key in access.ingestion_key. It signs the agent in to OpAMP and to OTLP. No foreign key:
    -- modules do not reference each other's tables.
    ingestion_key_id  uuid        NOT NULL UNIQUE,
    created_at        timestamptz NOT NULL,
    revoked_at        timestamptz,

    -- What the agent last reported over OpAMP. Empty until it first connects.
    instance_uid      bytea,
    version           text,
    host_name         text,
    os                text,
    healthy           boolean,
    health_error      text,
    -- The hash of the configuration it reports, and Veyra.Agents.Agents.AgentConfigState for it.
    config_hash       bytea,
    config_state      smallint    NOT NULL DEFAULT 0,
    config_error      text,
    last_seen_at      timestamptz
);

-- One built-in agent at a time. A revoked one stays for its history.
CREATE UNIQUE INDEX agent_one_built_in ON agents.agent (placement) WHERE placement = 1 AND revoked_at IS NULL;

CREATE TABLE agents.sql_server (
    id           uuid        PRIMARY KEY,
    -- The server's name in Veyra: its database block, server.address in its telemetry.
    name         text        NOT NULL UNIQUE,
    address      text        NOT NULL,
    port         integer     NOT NULL,
    environment  text        NOT NULL,
    agent_id     uuid        NOT NULL REFERENCES agents.agent (id),
    login        text        NOT NULL,
    -- Encrypted with ASP.NET Core Data Protection, purpose Veyra.Agents.SqlServerPassword.v1. Never sent back.
    password     text        NOT NULL,
    -- Veyra.Agents.SqlServers.SqlServerEncryption: 1 required, 2 trust the server's certificate, 3 off.
    encryption   smallint    NOT NULL,
    created_at   timestamptz NOT NULL,
    changed_at   timestamptz NOT NULL
);

CREATE INDEX sql_server_agent ON agents.sql_server (agent_id);
