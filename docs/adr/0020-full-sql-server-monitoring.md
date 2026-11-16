# 0020. Full SQL Server monitoring: scope, a live view, a drill-down, and a store for database activity

Date: 2026-11-15
Status: Accepted. Supersedes the part of ADR 0018 that shows running and top queries from the receiver's events.

## Context

The SQL Server page of ADR 0018 and VEY-43 shows the server's health: batch requests, waits, blocking, deadlocks,
databases with their backups, the queries running now, and the top queries over the range. A person who works with
their databases every day judged it of **no real use** (VEY-44):

- **It is not live.** Numbers arrive every 15 to 60 seconds, and the page does not refresh by itself.
- **There is no drill-down.** A top query is a line of text. Nothing answers the next question: since when is it
  slow, which plan does it use, what does it wait on, who runs it, what did it block.

The requirement is **full SQL Server monitoring**: what a DBA expects from a dedicated SQL Server monitoring tool,
from what runs this second to backups, integrity, availability, configuration, and security, with a Simple view for
the people who own the applications.

Facts that bound the answer:

- **The receiver gives health, not reasons.** The OpenTelemetry `sqlserver` receiver sends performance counters,
  waits per type, file I/O, and samples of running requests. Everything else is read through SQL Server's dynamic
  management views (DMVs), catalog views, `msdb`, and the built-in `system_health` session.
- **Running requests miss the usual culprit.** The receiver's query samples come from `sys.dm_exec_requests`. A
  session that sleeps with an open transaction holds its locks but runs no request, so the head of a blocking chain is
  often invisible.
- **The plan cache is live but forgets.** `sys.dm_exec_query_stats` counts every cached plan as it runs. A plan
  leaves it when evicted, recompiled, or when the server restarts, and `OPTION (RECOMPILE)` plans are never kept.
- **Query Store remembers but is not live.** It keeps each query's runtime statistics, plans, and, from SQL Server
  2017, waits, per database, over restarts. Its default interval is 60 minutes, and it is off on databases created
  before SQL Server 2022 unless someone turned it on.
- **`system_health` already records the rare events.** It runs on every SQL Server and Managed Instance and keeps
  deadlock graphs, errors of severity 20 and above, memory errors, non-yielding schedulers, lock waits over 30
  seconds, latch waits over 15 seconds, and connectivity and security ring buffer records, in a few rolling files.
  Azure SQL Database has no such session.
- **Statement statistics do not fit metric series.** VEY-43 sends four series per statement, with the statement's
  text as an attribute. All metrics share one series limit (50,000, ADR 0010). A busy server meets thousands of
  distinct statements a week, and plans, sessions, and deadlock graphs are not numbers at all.
- **The grants already given cover almost everything.** The login of the SQL Server guide has `VIEW SERVER STATE` and
  `VIEW ANY DEFINITION`. `VIEW SERVER STATE` implies `VIEW DATABASE STATE` in every database, which Query Store's views
  need; on SQL Server 2022 and later it implies `VIEW SERVER PERFORMANCE STATE` and `VIEW SERVER SECURITY STATE`, which
  the DMVs, the `system_health` files, and `sys.dm_server_services` need there. `CONNECT ANY DATABASE` lets the login
  into every database, present and future, without any further rights. Statistics details
  (`sys.dm_db_stats_properties`) are the exception: they need `SELECT` on the table, and return nothing without it.
- **The agent can run any read, on a schedule only.** The `sqlquery` receiver runs SQL on a fixed interval per
  receiver and sends rows as metrics or as logs. Logs can carry large text, such as a plan's XML, in the body, and a
  tracking column makes a query read only rows newer than the last one sent. The logs part is marked *development*,
  one step below the metrics part.
- **Veyra cannot ask the server anything on demand.** In another network only the agent reaches the server, and it
  connects out (ADR 0019). The upstream Collector has no way to run a query when Veyra asks; a changed configuration
  restarts it.

## Decision

Veyra monitors SQL Server **completely**: the fourteen areas below, each with its reads, its pages, its findings, and
its alert rules. It gives a **live view** refreshed every 10 seconds, a **drill-down** from every figure to its
details, a **Simple** view in words for application owners and an **Advanced** view for DBAs, and **history** from
Query Store and Veyra's own store. The agent reads it all with read-only queries on a schedule, and Veyra keeps it in
a **new store for database activity** instead of in metric series.

### Principles

- **The agent stays as it is.** The `sqlquery` receiver is part of the official `otelcol-contrib` build that the
  agent already runs (ADR 0019), and reads the statements of VEY-43 today. Every new read is a query in the
  configuration Veyra generates and sends over OpAMP; no code is added to the agent, and no agent of Veyra's own is
  built. A new agent version is needed only when Veyra moves its pinned Collector version.
- **Read-only, nothing installed, nothing changed.** Only `SELECT`s in the agent's template, marked
  `/* veyra-agent */` as in VEY-43. No stored procedures, no Extended Events session of Veyra's own, no server setting
  changed, Query Store not turned on by Veyra. Where a change would help, such as turning Query Store on, creating a
  missing index, or ending a session, Veyra writes the script and a person runs it, as with the login (ADR 0019).
- **Findings, not only figures.** Veyra judges the data with rules and writes **findings**: a sentence, its severity,
  its evidence, and its fix, with a script where one helps. Findings lead the Simple view, mark the Advanced view,
  and become alerts where a built-in rule says so (ADR 0012).
- **Usual is measured.** "Slower than usual" and "longer than usual" compare with a **baseline**: the same hour of
  the week over the last four weeks, from hourly rollups. Until four weeks exist, the baseline uses what there is and
  says so.
- **Live means within 30 seconds**, usually 10 to 20: the agent's interval, the batch, and the page's refresh. The
  page says how old its newest data is.
- **Every query has a cost budget**, measured in CI on SQL Server 2019, 2022, and 2025 with a filled plan cache.
  The agent's own work stays visible on the page, as VEY-43 does now.

### Complete scope

Each area lists what Veyra shows, where it comes from, how often it is read, and the findings and alert rules it
brings. Cadences are defaults; each can be lengthened per server on the Settings screen.

#### 1. Server and instance

- **Shows:** version, edition, cumulative update and its support end date, uptime and restarts, service accounts and
  whether SQL Server Agent runs, CPU used by SQL Server and by everything else on the machine, schedulers (runnable
  tasks, worker threads), memory (target and total, the largest memory clerks, page life per NUMA node, the
  operating system's memory state, memory grants waiting), connections by program, host, and login, compilations and
  recompilations, and memory dumps.
- **From:** the receiver's counters, `sys.dm_os_sys_info`, `sys.dm_server_services`, `sys.dm_os_schedulers`,
  `sys.dm_os_memory_clerks`, `sys.dm_os_sys_memory`, `sys.dm_exec_query_resource_semaphores`,
  `sys.dm_server_memory_dumps`, and the scheduler monitor in `sys.dm_os_ring_buffers` for SQL Server's share of CPU.
  Microsoft marks that ring buffer as not supported; it has kept its shape since SQL Server 2008, and CI catches a
  change. Where an agent also watches the machine itself, its host CPU is shown beside it.
- **Every:** 1 minute; clerks and dumps 15 minutes.
- **Findings and rules:** high CPU, other processes taking CPU from SQL Server, worker threads running out, memory
  grants waiting, the operating system low on memory, a restart, a new memory dump, SQL Server Agent stopped, a
  build out of support.

#### 2. Waits

- **Shows:** waits per type and per category over time, signal and resource time apart, each type explained in
  words; the statements (Query Store) and sessions (samples) that waited on each.
- **From:** the receiver's wait statistics, `sys.dm_exec_session_wait_stats`, Query Store wait statistics, and the
  long lock and latch waits that `system_health` records.
- **Every:** 1 minute; sessions with the live samples.
- **Findings and rules:** a wait category far above its baseline, with its likely cause in words.

#### 3. Live activity

- **Shows:** every session that runs, blocks, is blocked, or holds a transaction open, with its request, wait,
  blocker, transaction age, tempdb use, memory grant, isolation level, program, host, login, and text; blocking
  chains with the head blocker first, even when it sleeps; long operations such as backups, restores, `DBCC`, and
  index rebuilds with their percent done and estimated end.
- **From:** `sys.dm_exec_sessions`, `sys.dm_exec_requests`, `sys.dm_os_waiting_tasks`,
  `sys.dm_tran_session_transactions`, `sys.dm_tran_database_transactions`, `sys.dm_db_session_space_usage`,
  `sys.dm_db_task_space_usage`, `sys.dm_exec_query_memory_grants`, `sys.dm_exec_input_buffer`. This read replaces the
  receiver's query samples.
- **Every:** 10 seconds, at most 200 sessions each time.
- **Findings and rules:** a head blocker older than a threshold, an open transaction older than a threshold, a
  session filling tempdb.

#### 4. Queries and plans

- **Shows:** every statement, stored procedure, trigger, and scalar function ranked over the range by total time, CPU,
  reads, writes, runs, or time per run; the query page with its history, plans, waits, and callers; plans as operator
  trees with their warnings; plan regressions; queries with many plans (parameter sensitivity); the plan cache's
  share of single-use plans; a comparison of two ranges, such as this week and last week.
- **From:** `sys.dm_exec_query_stats` by query hash and plan hash (runs, time, CPU, reads, writes, rows, spills,
  memory grants), `sys.dm_exec_procedure_stats`, `sys.dm_exec_trigger_stats`, `sys.dm_exec_function_stats`,
  `sys.dm_exec_text_query_plan`, `sys.dm_exec_cached_plans`, and Query Store per database.
- **Every:** statements 15 seconds; procedures, triggers, and functions 1 minute; plans of the 10 busiest statements
  and Query Store 15 minutes; plan cache composition 1 hour.
- **Findings and rules:** a plan regression, a statement slower than its baseline, implicit conversions, missing
  index warnings in plans, spills to tempdb, excessive memory grants, a plan cache full of single-use plans.

#### 5. Blocking and deadlocks

- **Shows:** blocking episodes with their chain, head blocker, duration, and statements; deadlocks drawn as graphs of
  sessions, resources, and the victim; lock escalations per table.
- **From:** episodes derived by Veyra from the live samples; `xml_deadlock_report` from the `system_health` files;
  `sys.dm_db_index_operational_stats` for escalations.
- **Every:** deadlocks 1 minute, newer than the last one sent; escalations 1 hour.
- **Findings and rules:** `db-blocking` and `db-deadlocks` of ADR 0018, now with the statements involved; frequent
  escalations on a table.

#### 6. Databases, files, and storage

- **Shows:** state, recovery model, compatibility level, and settings worth knowing (auto close, auto shrink, page
  verify, read committed snapshot, Query Store state, encryption); every file with its size, space used, growth
  setting, and maximum; the free space of each volume that holds files; the log's space used, what it waits for to be
  reused, and its virtual log file count; tempdb's files and version store; I/O latency per file; a forecast of when
  each file and volume fills.
- **From:** `sys.databases`, `sys.master_files`, `sys.dm_db_file_space_usage` and `FILEPROPERTY` per database,
  `sys.dm_os_volume_stats`, `sys.dm_db_log_space_usage`, `sys.dm_db_log_info`, `sys.dm_tran_version_store_space_usage`,
  and the receiver's file I/O.
- **Every:** 5 minutes; virtual log files 1 hour.
- **Findings and rules:** a database not online, a file or volume filling within the forecast window, a log that
  cannot be reused and why, too many virtual log files, growth set in percent, auto shrink or auto close on, page
  verify not `CHECKSUM`, tempdb files of unequal size.

#### 7. Backups and recovery

- **Shows:** per database, the last full, differential, and log backup, their size, duration, and destination; the
  time since the last log backup against the recovery model; whether the restore chain from the last full backup is
  unbroken; copy-only backups apart.
- **From:** `msdb.dbo.backupset` and `msdb.dbo.backupmediafamily`.
- **Every:** 15 minutes.
- **Findings and rules:** `db-backup-age` of ADR 0018; a new `db-log-backup-age` for databases in full or
  bulk-logged recovery; a broken log chain; a backup much slower than its baseline.

#### 8. Integrity

- **Shows:** the last good `CHECKDB` of each database, damaged pages, and errors 823, 824, and 825.
- **From:** `DATABASEPROPERTYEX(name, 'LastGoodCheckDbTime')`, `msdb.dbo.suspect_pages`, the `system_health` errors
  of severity 20 and above for 823 and 824, and the error events of area 11 for 825.
- **Every:** 15 minutes.
- **Findings and rules:** new `db-checkdb-age` and `db-damaged-pages` rules; any 823, 824, or 825 is a warning at once.

#### 9. Indexes and statistics

- **Shows:** missing indexes with their estimated gain and a `CREATE INDEX` script; unused indexes with their size and
  the writes they cost; duplicate and overlapping indexes; heaps; the age of statistics on large tables.
- **From:** the missing index DMVs, `sys.dm_db_index_usage_stats`, `sys.indexes`, `sys.index_columns`,
  `sys.dm_db_partition_stats`, and `STATS_DATE`. Rows changed since the last update come from `sys.sysindexes` if CI
  proves it readable with the grants below; otherwise they are left out.
- **Every:** 1 hour; index definitions once a day.
- **Findings:** an index worth adding, an index worth dropping, duplicates, statistics older than their table's
  changes. Index fragmentation is not read: `sys.dm_db_index_physical_stats` scans the indexes it measures.

#### 10. SQL Server Agent

- **Shows:** every job with its schedule, next run, runs, steps, durations, and outcome; jobs running now and for how
  long; whether the Agent service runs.
- **From:** `msdb.dbo.sysjobs`, `sysjobsteps`, `sysjobhistory`, `sysjobactivity`, `sysjobschedules`, `sysschedules`,
  and `sys.dm_server_services`.
- **Every:** 1 minute.
- **Findings and rules:** `db-agent-job` of ADR 0018; a job running much longer than its baseline; a job that did not
  run when scheduled; the Agent service stopped.

#### 11. Errors and events

- **Shows:** SQL Server's errors and notable events as log records of the server's block, in the Log Explorer and on
  an Events tab: severe errors, memory errors, non-yielding schedulers, connectivity errors, failed logins, and long
  waits.
- **From:** the `system_health` files, read by the agent like deadlocks. Where an agent also runs on the server's own
  machine (VEY-35), the Windows Application Event Log or, on Linux, the error log file adds every other message,
  error 825 and failed logins among them. Reading the error log through `xp_readerrorlog` is not used: it needs
  rights far beyond monitoring.
- **Every:** 1 minute.
- **Findings and rules:** a severe error, a burst of failed logins, a memory error.

#### 12. Availability and disaster recovery

- **Shows:** Availability Groups with each replica's role, health, and synchronization, each database's send and redo
  queue and estimated data loss, and failovers as events; log shipping with the last backup, copy, and restore and
  their delay; the active node of a failover cluster instance.
- **From:** `sys.availability_groups`, `sys.dm_hadr_availability_replica_states`,
  `sys.dm_hadr_database_replica_states`, `msdb.dbo.log_shipping_monitor_primary` and `_secondary`, and
  `sys.dm_os_cluster_nodes`.
- **Every:** 1 minute.
- **Findings and rules:** a replica not healthy or not synchronizing, a queue above its threshold, a failover, log
  shipping behind its threshold.

#### 13. Configuration and security

- **Shows:** server settings with their recommended values (maximum server memory, max degree of parallelism, cost
  threshold for parallelism, optimize for ad hoc workloads) and every change to them over time; and a security posture:
  whether `sa` is enabled, logins without password policy, members of `sysadmin` and their changes, `xp_cmdshell` and
  CLR enabled, databases marked `TRUSTWORTHY`, `guest` enabled in user databases, certificates near expiry, and
  connections without encryption.
- **From:** `sys.configurations`, `sys.server_principals`, `sys.sql_logins`, `sys.server_role_members`,
  `sys.databases`, `sys.database_permissions` per database, `sys.certificates`, and `sys.dm_exec_connections`.
- **Every:** 1 hour; changes are found by comparing with the previous read.
- **Findings:** each setting off its recommended value with the reason, each security finding with its fix script; a
  configuration or `sysadmin` change is recorded as an event.

#### 14. Capacity and trends

- **Shows:** growth of data, logs, and volumes with the date each fills, CPU, memory, and workload against their
  baselines over weeks, and which databases use the most CPU, reads, and time.
- **From:** the hourly rollups of the areas above, and statements grouped by database.
- **Every:** computed by Veyra from what is stored.
- **Findings:** a volume or file that fills within 30 days, a workload growing faster than its trend.

**Not in scope** of this decision, each for a later ADR if asked for: replication, SQL Server Integration Services,
Reporting Services, and Analysis Services, database mirroring (deprecated), forcing plans or changing anything on the
server, and Veyra's own Extended Events sessions.

**Azure SQL.** Managed Instance is read like SQL Server, except that its backups, cluster, and host belong to Azure.
Azure SQL Database is read per database, with `##MS_ServerStateReader##` or `VIEW DATABASE STATE`, without SQL Server
Agent, `msdb` backups, or `system_health`; deadlocks there need a database-scoped Extended Events session, which Veyra
writes as a script for a person to create. Both come last in the order of work.

### How the agent reads it

- **Each interval is one `sqlquery` receiver** in the generated configuration, since the interval belongs to the
  receiver: 10 seconds, 15 seconds, 1 minute, 5 minutes, 15 minutes, 1 hour, and 1 day.
- **Per-database reads**, such as Query Store, file space, index names, and permissions, run in one receiver per
  database and interval, its connection naming the database. Veyra writes them from the databases the server reports,
  user databases only, up to 50 by default; a new database is picked up by itself.
- **Rows become logs or metrics.** Figures that are the same few series every time, such as file sizes, backup ages,
  and queue sizes, are metrics in the existing store. Everything keyed by a statement, plan, session, or event is a
  log record named for its kind, such as `sqlserver.session`, `sqlserver.plan`, or `sqlserver.deadlock`, with large
  text in the body, a plan up to 2 MB.
- **Events are read once.** Deadlocks and `system_health` events use the tracking column, so only events newer than
  the last one sent are read; Veyra also drops any it already has.
- The exact names, attributes, and units become part of the documented contract in `docs/integrations/sqlserver.md`,
  as ADR 0018's names are.

**The login.** The script on the Database monitoring page grants, besides what it grants today:

- `CONNECT ANY DATABASE`, for the per-database reads;
- in `msdb`, `SELECT` on `backupmediafamily`, `sysjobsteps`, `sysjobactivity`, `sysjobschedules`, `sysschedules`,
  `suspect_pages`, `log_shipping_monitor_primary`, and `log_shipping_monitor_secondary`.

Nothing grants access to the data in user tables. A server whose login lacks a newer grant keeps working; the areas
that need it say "A grant is missing" with the script.

### Where it is kept

A new store for **database activity**, owned by the `Storage` module, with its own schema and migrations. Its tables
are engine-neutral, so PostgreSQL's `pg_stat_statements` and `pg_stat_activity` can use them (VEY-33).

| Table | Holds | Kept |
| --- | --- | --- |
| Statement statistics | Per server, database, query and plan hash, interval | 48 h as sent, then hourly for 30 days |
| Statement texts | One per query hash, up to 4,000 characters, first and last seen | while statistics refer to it |
| Plans | One per plan hash, compressed, with its operators, costs, and warnings | while statistics refer to it |
| Session samples | Every 10-second sample | 7 days |
| Blocking episodes | Head blocker, chain, start, end, longest wait | 90 days |
| Deadlocks | The graph, its victim, resources, and statements | 90 days |
| Query Store intervals | As Query Store reports them, per database | 30 days |
| Snapshots | Configuration, security, index, and job definitions, kept when they change | 1 year |
| Findings | Each finding with its rule, subject, severity, evidence, first and last seen | 90 days after it ends |
| Baselines | Per figure, hour of the week, the last four weeks' values | rolling |

- **Ingestion routes by name.** OTLP stays the only way in. Ingestion recognizes the names above and hands the
  records to a writer defined in `Veyra.Core` and implemented by `Storage`. Error events of area 11 stay ordinary log
  records of the server's block. The statement series of VEY-43 are no longer written as metric series and age out.
- Retention and limits are settings on the Settings screen (ADR 0017), under Data and retention.

### Screens

The database screen gets tabs: **Live**, **Queries**, **Waits**, **Blocking**, **Databases**, **Jobs**, **Events**,
and **Server**. Databases open a database page; Server holds configuration, security, availability, and capacity.

- **Live** refreshes every 10 seconds while the tab is visible: the server's tiles, the blocking tree with its head
  blocker on top, even when it sleeps, with its open transaction's age, every running request, and long operations
  with their progress. A session opens a panel with its full text, its plan, and its transaction, and the `KILL`
  statement to copy.
- **Queries** ranks statements, procedures, triggers, and functions. A statement opens its **query page**: its text,
  formatted; charts of runs, time per run, CPU, reads, rows, and spills, with the **plan it used** as bands, so a plan
  change stands out ("Since 14:20 on plan 2: 6.1 s a run, was 0.4 s"); its waits; its callers by program, host, and
  login; each plan as an operator tree with warnings in words and a `.sqlplan` download; and the blocking and
  deadlocks it took part in.
- **Waits** shows wait types over time, each explained, with the statements and sessions behind them.
- **Blocking** lists episodes and deadlocks; a deadlock is drawn as its graph.
- **A database page** shows its files, volumes, log, backups and restore chain, integrity, settings, Query Store
  state with the script to turn it on, index advice with scripts, statistics, and its top statements.
- **Jobs** lists jobs, runs, and steps with durations against their baselines.
- **Events** lists SQL Server's errors and events, filtered like the Log Explorer, which they also appear in.
- **Server** shows the instance, memory, schedulers, configuration with its history, the security posture,
  Availability Groups, log shipping, and capacity forecasts.

### Simple and Advanced

The toggle of the monitoring screens (VEY-37) splits the pages by **who reads them**, not by hiding a few tiles.

- **Simple** is for the developer or operator who owns an application but is not a DBA. It answers three questions
  in words: is the server well, what is wrong, and what can I do. Veyra's findings come first, as sentences; numbers
  follow only where they support a finding. Wait types, plan operators, and SQL Server's own terms are translated:
  "waits for a lock held by session 88", not `LCK_M_U`. A statement is named by what it does, from its verb, table,
  and filter, such as "Update of Orders by CustomerId", with the SQL one click away.
- **Advanced** is for the DBA. It shows every figure SQL Server gives, with SQL Server's own names, every column,
  ranking by any measure, and the raw material: plans as operator trees, deadlock graphs, Query Store intervals, and
  XML to download.

Per page, what Simple shows and what Advanced adds:

- **Server status.** Simple: a status sentence, the findings worst first, and four tiles: requests, CPU, sessions
  waiting on others, and the longest request. Advanced: every tile, wait types, sessions, memory, tempdb, and I/O.
- **Live.** Simple: blocking as a sentence and a chain of programs and hosts; running requests with their time and
  what they wait for, in words. Advanced: the blocking tree with wait types and resources, and every column of a
  request: CPU, reads, tempdb, memory grant, isolation level.
- **Queries.** Simple: **queries with findings** only, such as got slower, plan changed, missing index, implicit
  conversion, or spills, each with its fix in words. Advanced: every statement ranked by any measure, with a line of
  each, filters, and the comparison of two ranges.
- **Query page.** Simple: the finding, the fix, time per run over the range with the change marked, and who runs it.
  Advanced: every per-run figure, waits, plans side by side, the operator tree, warnings, Query Store intervals, and
  the `.sqlplan` download.
- **Waits.** Simple: no tab; the status sentence names a dominant wait in words. Advanced: the Waits tab.
- **Blocking.** Simple: episodes as sentences, who blocked whom, for how long, with which statement. Advanced: the
  chains with resources and wait types, and deadlock graphs.
- **Databases.** Simple: size, growth and when it fills, backups, the last integrity check, and problems in words.
  Advanced: files, volumes, log reuse and virtual log files, I/O latency, settings, Query Store, index advice, and
  statistics.
- **Jobs.** Simple: failed, late, and overrunning jobs, and when they run next. Advanced: every job with its history,
  steps, and durations.
- **Events.** Simple: the severe events in words. Advanced: every event with its fields.
- **Server.** Simple: settings and security findings with their fixes, and whether availability is healthy.
  Advanced: every setting and its history, the full security posture, replicas and queues, and forecasts.

Findings are written once, in Veyra, from the same data; the Simple and Advanced views show the same finding with
less or more of its evidence. A Simple page links to its Advanced form for whoever wants the evidence.

### Everything leads somewhere

Every value on these pages does one of three things, and looks like it does:

- **Opens its detail.** A query's text opens its query page, a session its panel, a plan its tree, a deadlock its
  graph, a database its page, a job its history, an event its record. A tile opens what it counts: Blocked opens
  Blocking, CPU opens the ranking by CPU, Oldest open transaction opens that session.
- **Filters.** A database, program, host, login, wait type, plan, or job is a chip. Clicking it filters the whole
  page, and the filters stand in a bar at the top, each removable. Filters and the range are in the address, so every
  view is a link to share.
- **Explains.** A wait type, a plan operator, a warning, a setting, or a SQL Server term opens a short popup: what it
  means, what usually causes it, and the actions above, such as "Filter by LCK_M_U" or "Queries that waited on it".

Charts take part too: dragging across a chart narrows the range, a plan band selects that plan, and a point opens
the minute behind it. Every link, chip, and popup works by keyboard: Tab reaches it, Enter opens it, Escape closes it.

### Settings

Settings → Database monitoring gets an **Analysis** panel per server: each area with its switch, its interval, the
databases it reads, and its measured cost on the server; thresholds of the new rules live under Alert rules, as all
rules do (ADR 0017). The Simple and Advanced toggle does not apply there.

### Cost on the server

- The live reads cost the most often and are kept small: sessions every 10 seconds, statements every 15. Heavy reads,
  such as plans, Query Store, indexes, and configuration, run every 15 minutes to once a day.
- Index fragmentation is not read at all, and no read scans user tables.
- CI measures each read's CPU and reads on all three versions with a filled plan cache and fails above its budget.

### Order of work

Each step is one work item with its own branch, mockups, CI checks against SQL Server 2019, 2022, and 2025 with a
test workload that produces its scenarios, documentation, and the alert rules and findings of its areas.

1. **Foundation and Live** (areas 3, 1 in part): the activity store, the findings and baseline engine, the tabs, the
   Simple and Advanced split, chips, filters, and popups, the sessions read, and the Live tab.
2. **Queries and plans** (area 4 without Query Store): statements, procedures, triggers, and functions, plans, the
   Queries tab, and the query page.
3. **Blocking and deadlocks** (area 5): episodes, deadlock graphs, and lock escalations.
4. **Query Store** (area 4): per-database reads, history beyond 48 hours, regressions, waits per query, parameter
   sensitivity, and the `CONNECT ANY DATABASE` grant.
5. **Databases and storage** (area 6): files, volumes, log, virtual log files, settings, forecasts, and the database
   page.
6. **Backups, recovery, and integrity** (areas 7 and 8): log backups, the restore chain, `CHECKDB`, and damaged pages.
7. **Indexes and statistics** (area 9).
8. **Server, waits, and capacity** (areas 1, 2, and 14): CPU, schedulers, memory, the Waits tab, and the Server tab's
   capacity.
9. **SQL Server Agent** (area 10): steps, durations against baselines, running and late jobs, and the Agent service.
10. **Errors and events** (area 11): `system_health` events, the Event Log or error log through a local agent, and
    the Events tab.
11. **Availability** (area 12): Availability Groups, log shipping, and failover cluster instances.
12. **Configuration and security** (area 13).
13. **Azure SQL Managed Instance and Azure SQL Database.**

Steps 1 to 4 make the page useful for daily work; each later step adds an area without changing the earlier ones.

## Alternatives considered

- **Veyra connects to SQL Server itself** for the live view and to fetch details on demand. Truly immediate, but it
  fails for servers in another network, which only the agent reaches (ADR 0019), and Veyra would hold open
  connections to every server.
- **An agent of Veyra's own, or a custom Collector build, to read the DMVs.** Not needed: the upstream `sqlquery`
  receiver runs any `SELECT` on a schedule. Owning an agent would mean releases, signing, and installers to maintain
  for every platform, for no read the upstream one cannot make.
- **A custom Collector component** that runs a query when Veyra asks, through OpAMP custom messages. On-demand plans
  and sessions, but ADR 0019 keeps the agent to upstream builds. It may come with a later ADR if fixed intervals prove
  too slow.
- **Statements stay as metric series.** No new store, but texts in attributes, thousands of series per server, and
  no place for plans, sessions, or graphs.
- **The receiver's query samples stay the live source.** No new query, but sleeping head blockers stay invisible.
- **Community procedures** such as `sp_WhoIsActive` or the Blitz scripts. Proven and rich, but installed on every
  server and run with wider rights.
- **Veyra's own Extended Events session, or the blocked process report.** Richer events, but both change the server's
  configuration. The built-in `system_health` session already records deadlocks, severe errors, and long waits.
- **Reading the error log with `xp_readerrorlog`.** Every message from anywhere, but only for logins with
  `securityadmin` rights. The Event Log or log file through a local agent gives the same without them.
- **Query Store only.** It survives restarts and evictions, but it is per database, often off, and an hour behind by
  default. It complements the plan cache; it cannot replace it.
- **Pushing updates to the browser** over WebSocket or server-sent events. Polling every 10 seconds is enough when
  the data itself arrives every 10 to 15 seconds.

## Consequences

- The SQL Server page becomes a set of pages, and the API grows endpoints for queries, sessions, blocking, deadlocks,
  databases, jobs, events, and the server, each within ADR 0015's budgets.
- A new store with its own schema and migrations, a writer interface in `Veyra.Core`, and a findings and baseline
  engine that PostgreSQL (VEY-33) and later engines reuse.
- The agent's configuration grows to about seven `sqlquery` receivers per server, plus the per-database ones, all
  generated and tested.
- Existing servers need the login script run again for the new grants; until then the areas that need them say "A
  grant is missing" with the script, and the rest works.
- The agent sends more: about 5 to 20 MB a day for a quiet server, more for a busy one, mostly session samples and
  plans; the guide states the volume per area.
- The new rules named in each area join those of ADR 0012 and 0018, each with its settings and its own words in
  notifications. Findings that are not rules stay on the pages and do not notify.
- `docs/architecture/monitoring-scope.md` and `docs/integrations/sqlserver.md` describe the areas as each step lands;
  `README.md` lists them on the roadmap only as they ship.
- Untested yet, and proven in CI before the step that needs it:
  - the `sqlquery` logs part, marked *development*, at the pinned version;
  - the tracking parameter's syntax with the SQL Server driver;
  - a login with `CONNECT ANY DATABASE` and no user reading Query Store, file space, index names, and permissions in
    each database;
  - reading the `system_health` files, `sys.sql_logins`, and `sys.sysindexes` with the grants above on all three
    versions;
  - the scheduler monitor ring buffer's shape on all three versions.
