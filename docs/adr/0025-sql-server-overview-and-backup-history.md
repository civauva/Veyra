# 0025. A SQL Server's overview, backup history, and coverage

Date: 2026-11-26
Status: Accepted

## Context

The Console look rebuilds the SQL Server screens (VEY-69 step 4, reference screenshots 1, 3, and 8). One page shows a
server at a glance:

- **The header.** Version, CU, edition, OS, vCPU, RAM, uptime, and the number of databases.
- **Figures.** Each with its change against the period before.
- **Instance performance.** CPU, memory, and waits over the range.
- **Core counters.** Batch requests, compilations, page life, and waits, with their change.
- **Waits.** The top wait types.
- **Backups.** History by type, duration per day, a 7-day grid, and freshness per database.
- **Blocking.** The current chain and blocked sessions over the range.
- **Storage.** Size and growth over 24 hours, 7 days, and 30 days, the largest databases, and volumes.
- **Inventory.** Each database with its data and log size, growth, and state.
- **Coverage.** Which monitoring areas deliver data.

ADR 0020 decided what the agent reads. A survey of what exists (VEY-75) found:

- **Stored but never read.** SQL memory as a series (`sqlserver.memory.usage`), compilations
  (`sqlserver.batch.sql_compilation.rate`), I/O bytes per file and direction (`sqlserver.database.io`), and database
  sizes for 30 days.
- **Not read at all.** The machine's total memory and its operating system (`sys.dm_os_sys_info.physical_memory_kb`,
  `sys.dm_os_host_info`).
- **Only the latest backup.** Only the latest backup of each kind is known: its age, and for a full one its duration
  and size. There is no list of backups.
- **No comparison.** Nothing compares a database server's figures with the period before.
- **No coverage.** Nothing says which areas of a server deliver data.

Every answer meets the speed budgets of ADR 0015.

## Decision

### 1. The machine

- **What the agent adds.** The server read (`sqlquery/server`, every 60 s) adds the machine's physical memory and its
  operating system, from `sys.dm_os_sys_info` and `sys.dm_os_host_info`:
  - `sqlserver.os.memory.total`, in bytes
  - the attributes `os_name` and `os_release` on `sqlserver.server.info`
- **Azure SQL Database.** It has neither DMV, so it shows neither field.
- **Start time.** The instance's start time is worked out from the uptime the agent already sends.

### 2. Backup history

- **What the agent reads.** A new read, `sqlquery/backups`, every 5 minutes. It reads `msdb.dbo.backupset` for the
  backups that finished since the last read, at most 500 a read.
- **What each backup holds.** Database, type (full, differential, log), start, finish, size, compressed size,
  whether it was copy-only, and the device it went to.
- **How it is kept.** Each backup is a `sqlserver.backup` activity record in `storage.db_record`, keyed by its
  `backup_set_id`. That table already keeps records 90 days (ADR 0020).
- **Platforms.** Azure SQL Database and Managed Instance back up by themselves and have no `msdb` history to read.
  `PlatformReads` leaves the read out for them, and the screen says the platform backs up by itself.
- **What the server answers.** `GET /api/v1/metrics/databases/{engine}/{server}/backups?from=&to=` answers:
  - the backups of the window, newest first
  - the duration per day and type
  - a grid of the last 7 days: each database, each day, and the types that ran
  - each database's freshness
- **Freshness.** It is judged against the backup rules' thresholds (`DatabaseBackupAgeOptions` and the log backup
  rule). The screen and the alerts use the same thresholds. Only a database that never had a full backup differs: the
  screen counts it overdue, while the rule leaves it alone. A policy of its own per database is left for later (see
  Alternatives).

### 3. One read for a server's overview

`GET /api/v1/metrics/databases/{engine}/{server}/overview?from=&to=` answers the page in one request, as the Overview
does (ADR 0023):

- **The machine.** Version, CU, edition, OS, vCPUs, memory, start time, uptime, and the number of databases.
- **Figures.** Instance CPU, SQL memory against the machine's, active sessions, blocked sessions, total size, the age
  of the last full backup, batch requests per second, and the fullest volume's free share.
  - Each figure has its value now and a line over the range.
  - Where it changes over time, it also has its change against the period before. The comparison reuses
    `ResourceComparison` (ADR 0022).
- **Performance.** CPU, SQL memory, and wait time per second over the range.
- **Core counters.** Batch requests, compilations, page life expectancy, and wait time per second. Each is an average
  over the range, with its change against the period before.
- **I/O.** Reads and writes in MB/s, from the byte counters already stored.
- **Waits.** The top wait types of the range with their share; idle waits are left out.
- **Storage.**
  - Total size, with its growth over 24 hours, 7 days, and 30 days.
  - The largest databases, with their growth.
  - The volumes, with free space and days until full.
- **Inventory.** Each database with its data size, log size, growth per day, state, and recovery model.
- **Blocking.**
  - The current blocking chains from the latest live read.
  - Blocked sessions over the range, from `sqlserver.processes.blocked`.
  - Deadlocks of the range.
- **Coverage.**
  - **Which areas.** Each area of ADR 0020: activity, waits, queries, storage, backups, jobs, events, configuration,
    and availability.
  - **What each says.** Whether data arrived in the last 15 minutes (an hour for the hourly reads). If it did not, the
    permission that is missing, read from `/permissions`.

The existing endpoints of ADR 0020 stay as they are. The Console screens use them for the tabs this overview does not
replace: Live, Queries, Jobs, Events, and Server.

### 4. What the Console screens show

- **The tabs.** The server page has Overview, Blocking, Backups, Storage, Queries, Jobs, Events, and Server.
  - Overview reads `/overview`.
  - Blocking reads `/overview` and `/live`.
  - Backups reads `/backups`.
  - Storage reads `/overview` and `/storage`.
  - The rest use their existing endpoints.
- **The time.** The top bar's range applies to every tab (ADR 0022).

## Alternatives considered

- **Keep only the latest backup and count runs from it.** The latest backup says nothing about how long backups took
  on other days, or which logs ran. Those are the questions the backup history answers.
- **A backup policy per database, set on the Settings screen.** It is closer to what DBAs expect. But it adds a
  setting per database, and the alert rules would have to follow it. It is better done as its own change, with the
  rules. Until then, freshness follows the rules, so it never disagrees with an alert.
- **One endpoint per panel.** It would mean ten requests on every refresh, with figures from slightly different
  moments. ADR 0023 chose one read for the same reason.
- **Read the machine's memory from the host metrics.** It works only where the host also runs an agent with
  hostmetrics. SQL Server knows its machine on every platform that has the DMV.

## Consequences

- The agent template gains one read and two fields. An agent picks them up the next time Veyra sends it its
  configuration (ADR 0019).
- `storage.db_record` holds backups too. A busy server with 15-minute log backups of 50 databases writes about 4,800
  rows a day, well within the table's budget.
- Blocking episodes still reach back 7 days, the retention of live sessions. Longer episode history stays a known gap
  of ADR 0020.
- Freshness per database follows the alert rules until a policy per database is decided.
