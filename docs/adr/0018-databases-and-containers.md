# 0018. Databases, containers, and Windows hosts

Date: 2026-11-13
Status: Accepted; its running and top queries from the receiver's events superseded by 0020

## Context

v0.3 brings Docker containers, PostgreSQL, SQL Server, Windows hosts, and IIS (monitoring scope, README roadmap). All of
it arrives from the OpenTelemetry Collector. ADR 0007 already names containers and databases as kinds of block, but
only services and hosts exist so far: a block is whatever `service.name` or `host.name` says.

What the Collector contrib distribution 0.140 sends, captured from real containers in VEY-31 or read from the
receivers' definitions where Windows was needed:

- **Docker** (`docker_stats`): per container, CPU, memory with its limit, network, block I/O, restarts, and uptime.
  The resource holds `container.name`, `container.id`, and `container.image.name`, but **not the host**. The
  receiver must be told the Docker API version, or current engines refuse it.
- **PostgreSQL** (`postgresql`): connections, database and table sizes, deadlocks, rows live and dead, vacuums,
  locks, and more. The resource names the database, **not the server**, so two servers with a `shop` database cannot
  be told apart. Running and top queries arrive as log events (`db.server.query_sample`, `db.server.top_query`).
  Transaction ID wraparound has no metric.
- **SQL Server** (`sqlserver`): batch requests, waits, blocked processes, deadlocks, file I/O and latency, memory,
  connections. The resource holds the server as `service.instance.id` (`host:port`) and the database. Backup age,
  failed SQL Agent jobs, and database size are missing; they need `sqlquery`. Some units arrive with typographic
  quotes (`“KB”`).
- **Windows**: `hostmetrics` works as on Linux. `windowseventlog` sends Event Log entries as logs, without
  `host.name`. The `iis` receiver counts requests per method, connections, queues, rejected requests, and application
  pool state, but has **no latency and no status codes**; those are in IIS's W3C access logs.

In storage, a log record must have a service and falls back to `unknown_service`, and a metric series knows only its
service and host. Container logs, Event Log entries, and database events have no service, so they would all land in
`unknown_service`.

## Decision

Containers and database servers become **blocks of their own**, next to services and hosts. Every log record and
metric series records **which block it belongs to**, decided once at ingestion from its resource. The tested Collector
configurations in `integrations/` set the attributes that identity needs, and **new built-in rules** cover the
failures people ask about first. Their settings live on the Settings screen (ADR 0017).

### Which block telemetry belongs to

One rule in `Veyra.Core`, used for logs, metrics, and spans alike, picks the first that applies:

| Resource has | Block | Name |
| --- | --- | --- |
| `service.name` | service | the service name |
| `db.system.name` and `server.address` | database | engine and server, such as `postgresql pg-main:5432` |
| `container.name` | container | the container name, on its host |
| `host.name` | host | the host name |
| nothing of these | service | `unknown_service`, as today |

SQL Server's own `service.instance.id` (`host:port`) counts as `server.address` when that is missing.

- A database block is a **server**; its databases are parts, as a host's mount points are. A container belongs to
  the host in `host.name`, so the host screen lists its containers.
- `storage.log_record` and `storage.metric_series` gain `block_kind` and `block_name`, and log records gain
  `host_name`. Each is indexed with time. Existing records count as their service.
- The Log Explorer filters by block, of any kind, instead of only by service.
- An application's own telemetry keeps its service even when it runs in a container: the SDK sets `service.name`,
  and the container's stats arrive separately from `docker_stats`.

### What the Collector configurations set

Each target has a tested `collector.yaml` in `integrations/` with a guide in `docs/integrations/`, like Linux and
traces. Beyond the receivers, they:

- add `host.name` with the `resourcedetection` processor to container stats, container logs, and Event Log entries;
- add `db.system.name` and `server.address` to PostgreSQL telemetry, and `server.address` to SQL Server's, from a
  server name the person chooses;
- set the Docker API version and SQL Server's encryption settings, and say how to trust a self-signed certificate;
- read Docker's JSON log files with the container name as the log tag, so container logs carry `container.name`;
- map the Windows Event Log level to log severity (Critical and Error to error), and read the System and Application
  channels;
- read IIS's W3C logs per site with `service.name` set to the site, status 5xx as errors, and `time-taken` as an
  attribute. IIS metrics carry the same `service.name`, so a site is a service with the existing error-rate rule;
- run `sqlquery` for what the receivers lack, under these metric names, which Veyra's rules and screens read:

| Metric | Unit | What it is |
| --- | --- | --- |
| `postgresql.database.xid.age` | {transactions} | Age of the oldest unfrozen transaction ID, per database |
| `postgresql.sessions.blocked` | {sessions} | Sessions waiting for a lock longer than 30 seconds |
| `postgresql.query.duration.max` | s | The longest running query now, per database |
| `sqlserver.database.size` | By | Data and log files, per database and file type |
| `sqlserver.database.backup.age` | s | Time since the last full backup, per database |
| `sqlserver.agent.job.failed` | {jobs} | 1 when a job's last run failed, per job |
| `sqlserver.query.duration.max` | s | The longest running request now, per database |

Other Collector setups work too, if they send the same attributes and names.

### New built-in rules

Each rule has its thresholds, a switch, and its clear condition as settings, like the rules of ADR 0012.

| Rule | Block | Measures | Caution | Warning | Clears |
| --- | --- | --- | --- | --- | --- |
| `container-restarts` | container | Restarts in the last 15 minutes | 2 | 5 | none for 15 minutes |
| `container-memory` | container | Memory in use of its limit, if it has one | 90 % | 95 % | 5 points below |
| `db-connections` | database | Connections in use of the maximum (PostgreSQL) | 80 % | 90 % | 5 points below |
| `db-blocking` | database | Sessions blocked, for 5 minutes | 1 | 5 | none for 5 minutes |
| `db-deadlocks` | database | Deadlocks in the last 15 minutes | 1 | 5 | none for 30 minutes |
| `db-long-query` | database, per database | The longest running query | 5 min | 30 min | below caution for 5 minutes |
| `db-wraparound` | database, per database | Transaction ID age, of the limit (PostgreSQL) | 50 % | 75 % | below 45 % |
| `db-backup-age` | database, per database | Time since the last full backup (SQL Server) | 26 h | 50 h | a new backup |
| `db-agent-job` | database, per job | The last run of a SQL Agent job failed | failed | — | the next run succeeds |
| `iis-app-pool` | service | The site's application pool is not running | — | stopped | running again |

- `db-backup-age` skips `tempdb` and databases that were never backed up, and can be switched off for databases that
  are not backed up on purpose, by name, on the Settings screen.
- Alerts name the block and part in words, for example "SQL Server sql-prod-01: shop has no backup for 31 h".
- Disk space for database files stays with the host's disk rules.

### Screens

The design is in the VEY-31 mockups.

- **Metrics** lists Databases and Containers beside Services and Hosts.
- **A database screen** (`/metrics/databases/{engine}/{server}`) shows:
  - the server's connections, waits or locks, deadlocks, and blocked sessions;
  - each database with its size and growth per day, and its backup age or transaction ID age;
  - the queries running now and the top queries by total time, from the receivers' events;
  - SQL Agent jobs with their last runs.
- **The host screen** adds its containers (CPU, memory of the limit, restarts, uptime), the top processes, and
  recent issues: Event Log errors on Windows, failed units on Linux.
- **The home** draws databases and containers with alerts as blocks, like hosts.

### Speed

Every new screen and rule meets ADR 0015's budgets: a database or host screen in 100 ms over its window, reading the
minute rollups, and a block filter in the Log Explorer like a service filter today. Plan tests prove the new indexes
are used.

## Alternatives considered

- **Containers as parts of their host only.** No new block kind, but rules, alerts, and links would all need a host
  and a part, and a container's logs would have nowhere to go.
- **Setting `service.name` for databases and containers in the Collector.** No storage change, but databases and
  containers would look like services everywhere, and the error-rate rule would judge Event Log noise as a
  service's errors.
- **Reading the Docker log files in Veyra.** Simple for one host, but Veyra runs in its own container and must not
  need the Docker socket or the host's files (ADR 0006).
- **Query samples as the source of the long-query rule.** No `sqlquery` needed, but rules would read log events, and
  sampling every 10 seconds stores a lot of query text. A metric is cheaper to store and to check; the events are
  kept for the screen.
- **IIS latency from the IIS receiver.** It has none. The application's own SDK and the W3C log give it.

## Consequences

- One Storage migration adds the block columns and indexes to log records and metric series. The new columns start
  empty on old partitions, which still answer as their service.
- Ingestion derives the block for every resource; a test covers each row of the table above.
- The Log Explorer's service filter becomes a block filter; the REST API accepts `block` next to `service`, which
  stays for existing links.
- Ten rules join the four of ADR 0012, each with its settings on the Settings screen and its own words in
  notifications.
- `integrations/` gains `docker`, `postgres`, `sqlserver`, and `windows`, each tested against a real Collector in CI
  where a container can run it, and the metric names above become part of Veyra's documented contract.
