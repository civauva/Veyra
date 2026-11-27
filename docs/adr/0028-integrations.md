# 0028. Integrations: setup wizard, modules, probes through the agent, and versioned templates

Date: 2026-11-26
Status: Accepted

## Context

The Console look's Integrations (VEY-69 step 7, reference screenshots 2 and 4) turn a monitored SQL Server into a
managed integration:

- **A setup wizard.** Connection, Permissions, Modules each with an interval, Validation, and Installation, with a
  Test connection that answers the version, edition, and databases before anything is saved.
- **A package page.**
  - the template's version, and when and by whom it was set
  - the collector's health and uptime, and configuration validation with Validate now
  - metrics collected per module, active checks with each query's latency, and package information
  - update, roll back, remove, and next steps

A survey (VEY-78) found:

- **Adding a server.** A SQL Server is added on the Settings screen in three steps. "Check it" runs only after saving,
  from the state the agent's reads imply.
- **Connections.** Only SQL authentication to `host:port` works: no named instance, no Windows authentication.
- **Intervals.** Every receiver's interval is fixed in the template. ADR 0020's per-server area switches were never
  built.
- **Asking the agent.** The OpAMP server delivers configuration and reads health. It cannot ask an agent to run
  anything and reply, and agents stay upstream builds (ADR 0019).
- **Versions.** The template has no version; it moves with Veyra's release.
- **Query times.** The agent's own metrics are not collected. The time SQL Server spends on each of Veyra's reads is
  already in the statement statistics, marked `veyra-agent: name`.

The owner chose two of the paths below: probes through the agent, and versioned templates.

## Decision

### 1. Connection

- **Named instances.** A server may name an instance (`HOST\INSTANCE`). The connection then uses the instance and the
  SQL Server Browser instead of a port.
- **Authentication.** It is one of:
  - **SQL**: a login and password, as today
  - **Windows (NTLM)**: a domain login and password, from any agent
  - **Windows (integrated)**: the agent's service account, only for an agent on Windows
- **Passwords.** They stay encrypted with Data Protection, as today.

### 2. Modules and intervals per server

- **What a module is.** The template's reads are grouped into modules:
  - **Core**: performance and health, waits, server
  - **Activity and blocking**: live
  - **Queries**: statements, plans, Query Store
  - **Storage**: files and volumes
  - **Backups**
  - **Jobs**
  - **Events**
  - **Configuration**
  - **Indexes**
- **What each server stores.** Each module's switch and interval, with each module's own default and its allowed
  range. Core cannot be switched off.
- **How it reaches the agent.** `CollectorConfig` writes each server's receivers with its own intervals, and leaves
  out the modules that are off. The permissions read always runs.

### 3. Probes through the agent

- **What a probe is.** A short-lived check that the chosen agent runs: Test connection before saving, and Validate now
  on a saved integration.
- **How it runs.**
  1. Veyra adds a probe receiver to that agent's configuration. The receiver reads version, edition, platform, the
     databases, and the missing permissions, with the given connection.
  2. The agent sends the result as a `sqlserver.probe` record with the probe's ID.
  3. Veyra removes the receiver when the result arrives, or after 2 minutes.
- **The API.**
  - `POST /api/v1/integrations/probes` with the connection, unsaved, starts a probe.
  - `GET /api/v1/integrations/probes/{id}` answers pending, its result, or why it failed: no agent, unreachable,
    login failed, certificate rejected, or timed out.
- **Unsaved credentials.** The password of a probe is encrypted while the probe lives and is forgotten after it.
- **The cost.** A probe takes 30 to 60 seconds. The agent's Collector applies the new configuration twice, which
  pauses its other reads for a few seconds each time.

### 4. Versioned templates

- **The version.** Each integration template carries a version in its header, such as `sqlserver 0.2.0`.
- **What Veyra ships.** The current version and the one before it, embedded.
- **What a server records.** Its template version, and when and by whom it was set. New servers take the current
  version.
- **Update and roll back.** On the package page they move a server to the current or the previous version. The change
  is recorded in the monitoring history with the versions.
- **What does not change.** Agent binaries stay pinned to Veyra's release (ADR 0019); only the template is versioned.

### 5. The package page's figures

- **Collector health and uptime.** From OpAMP health, whose start time is now stored.
- **Configuration validation.** The agent's remote config status, and the last probe with its time.
- **Collected metrics per module.** Series and points of the module's metrics over the range.
- **Active checks.** The time SQL Server spends per run on each of the integration's reads, from the statement
  statistics marked `veyra-agent: name`.
- **Package information.**
  - the template version
  - the Collector version and the compatibility the template states
  - the least SQL Server version the template supports
  - the login and whether it lacks a permission
- **Next steps.** A checklist from what the installation still lacks: alert thresholds left at their defaults, no
  notification channel, and retention.

### 6. The screens

- **The routes.** One registry: `integrations` at `/integrations` under Manage, `integration` at
  `/integrations/sqlserver/:id`, and the wizard at `/integrations/sqlserver/new`.
- **Console.** It draws them as reference screenshots 2 and 4.
- **Forecast.** It keeps the Settings screen's database monitoring, which links to the same package page.

## Alternatives considered

- **Veyra connects directly to test.** It is fast, but it fails for every server behind a remote agent's network,
  which ADR 0020 rejected.
- **No test before saving.** It is simpler, but the wizard could only tell after the fact that a login or a
  certificate was wrong.
- **A custom Collector component answering OpAMP custom messages.** It would be immediate and leave the
  configuration alone, but it breaks the upstream-build rule of ADR 0019.
- **Full OpAMP packages.** Agents would download versioned configs and binaries, with update and roll back. It is the
  largest change and revisits ADR 0019; template versions give update and roll back without it.
- **The agent's own metrics for query latency.** They would need `own_metrics` and give scrape times per receiver,
  not per query. SQL Server's own statement statistics measure each read exactly.

## Consequences

- An agent's configuration grows with per-server intervals and module switches. The same input still gives the same
  text, so the hash only changes when something changed.
- Probes briefly restart the agent's Collector. The wizard says so before it tests.
- Two template versions ship with each release. A server on an older version than the previous one moves to the
  previous one at upgrade, recorded in the history.
- Named instances need UDP 1434 open from the agent. Integrated Windows authentication works only on Windows agents.
  The guide says both.
