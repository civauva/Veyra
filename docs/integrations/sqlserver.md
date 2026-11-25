# SQL Server

The OpenTelemetry Collector watches a SQL Server instance with its `sqlserver` and `sqlquery` receivers and sends
requests, waits, blocking, deadlocks, file I/O, database sizes, backup ages, SQL Agent jobs, and the queries running
now to Veyra over OTLP. Veyra shows the server as a database block with its own screen, and its built-in rules raise
alerts for blocking, deadlocks, long queries, missing backups, and failed jobs (ADR 0018). The tested configuration
is [`integrations/sqlserver/collector.yaml`](../../integrations/sqlserver/collector.yaml).

Tested with SQL Server 2019, 2022, and 2025 on Linux, and the Collector contrib distribution 0.140.0. CI runs the
check in [`integrations/sqlserver/test/`](../../integrations/sqlserver/test/run.sh) against each version on every
push.

There are two ways to set it up. **From the Settings screen** is the usual one: Veyra's built-in agent reads the
server, and nothing is edited by hand. **With your own Collector** is for people who already run one.

## From the Settings screen

When Veyra runs with its Docker Compose deployment, a second container beside it is Veyra's built-in agent: the
official Collector and its OpAMP supervisor, which Veyra sets up and configures by itself (ADR 0019). It reads every
SQL Server that Veyra's host can reach.

1. Open **Settings → Database monitoring**. Its top line says which agent reads the servers and whether it runs; when
   the agent has not started or needs attention, it says so there, before anything is added. Choose **Add SQL Server**.
2. **Where it is.** Give the server its name in Veyra, such as `sql-prod-01`; its history and alerts are filed under
   it, so keep it once chosen. Enter its address and port, and its environment.
3. **The login Veyra uses.** The page makes a strong password and writes the script that creates the login with
   exactly the grants of step 1 below. Copy it and run it once in SQL Server Management Studio as an administrator;
   only an administrator of the server can. Using a login of your own, type its password instead.
4. **Encryption.** *Required* checks the server's certificate against what the agent trusts. *Trust its certificate*
   accepts SQL Server's own self-signed one. *Off* encrypts only the login; use it only on a private network.
5. **Save and start watching.** The agent gets the new settings within half a minute. The panel then follows what it
   finds, line by line: reached the server, encrypted the connection, logged in, read every figure.

Each server on the page says how it stands, in words: *Receiving*, *Waiting for first data*, *Login failed*, *Not
reachable*, *Certificate refused*, *A grant is missing*, or a problem with the agent itself, each with what to do. The
reasons come from the agent's own logs, which it sends to Veyra like any telemetry.

The password is stored encrypted with Veyra's Data Protection keys and never shown again. Veyra sends it only to the
agent that reads that server, which keeps its configuration in a file only its own user can read. **Settings →
Agents** shows the agent: its version, when it last reported, and how many servers it reads. Under **How the
built-in agent runs** it has the commands to check, start, and read the agent on Veyra's server; it opens by itself
when the agent is not running.

For SQL Servers the built-in agent cannot reach, such as those behind a company firewall while Veyra runs elsewhere,
add an agent for their network under **Settings → Agents** and install it there with one line; see
[`agents.md`](agents.md#an-agent-in-another-network). Then choose it under **Read by**.

## Azure SQL Managed Instance and Azure SQL Database

Under **Kind**, the form also takes an Azure SQL Managed Instance or an Azure SQL Database (ADR 0020, VEY-57). The
agent then reads only what that platform has, and the SQL Server screen says what it leaves out.

- **Managed Instance** is read like SQL Server, with the same login script. Azure takes its backups and runs its
  storage, host, and cluster, so Veyra leaves out backups, volumes, log shipping, cluster nodes, and replicas; the
  Databases and Server tabs say so.
- **Azure SQL Database** is one database, read one at a time: name it under **Database**. Its login is a contained
  user, created in that database with `VIEW DATABASE STATE` and `VIEW DEFINITION`; the form writes the script, to run
  in the database as its administrator. Azure SQL Database has no SQL Server Agent, msdb, system_health, or
  server-wide views, so the Waits, Jobs, Events, and Server tabs are left out, with the reason under the tabs. The
  agent reads instead the database's share of its service tier (`sys.dm_db_resource_stats`) and its size
  (`integrations/sqlserver/azure-sql-database.yaml`).
- **Deadlocks on Azure SQL Database** are recorded only by a database-scoped Extended Events session. The same script
  creates `veyra_deadlocks`, which the agent reads like `system_health`.

Both are built and tested against the agent's configuration, not yet against live Azure instances.

## With your own Collector

### 1. Create a monitoring login

The Collector needs to read server state and SQL Agent's history in `msdb`, and nothing else. Run this as an
administrator, with a strong password of your own:

```sql
CREATE LOGIN veyra_monitor WITH PASSWORD = '<a strong password>';
GRANT VIEW SERVER STATE TO veyra_monitor;
GRANT VIEW ANY DEFINITION TO veyra_monitor;
GRANT CONNECT ANY DATABASE TO veyra_monitor;
GO

USE msdb;
CREATE USER veyra_monitor FOR LOGIN veyra_monitor;
GRANT SELECT ON dbo.backupset TO veyra_monitor;
GRANT SELECT ON dbo.sysjobs TO veyra_monitor;
GRANT SELECT ON dbo.sysjobhistory TO veyra_monitor;
GRANT EXECUTE ON dbo.agent_datetime TO veyra_monitor;
GRANT SELECT ON dbo.backupmediafamily TO veyra_monitor;
GRANT SELECT ON dbo.sysjobsteps TO veyra_monitor;
GRANT SELECT ON dbo.sysjobactivity TO veyra_monitor;
GRANT SELECT ON dbo.sysjobschedules TO veyra_monitor;
GRANT SELECT ON dbo.sysschedules TO veyra_monitor;
GRANT SELECT ON dbo.suspect_pages TO veyra_monitor;
GRANT SELECT ON dbo.log_shipping_monitor_primary TO veyra_monitor;
GRANT SELECT ON dbo.log_shipping_monitor_secondary TO veyra_monitor;
```

- `VIEW SERVER STATE` reads performance counters, waits, and running requests.
- `VIEW ANY DEFINITION` reads the size of every database's files.
- `CONNECT ANY DATABASE` lets the login into every database, present and future, to read Query Store, file space,
  and index names there (ADR 0020). It grants nothing inside the databases: no data of any table is readable.
- The `msdb` grants read backups and their destinations, damaged pages, SQL Agent jobs with their steps, schedules,
  and runs, and log shipping's state. A login created before ADR 0020 keeps working; run the new lines to add them.

The login can see query text, which may hold values from your applications. Treat Veyra's access like any other
reader of the server.

### 2. Create a key

Create an ingestion key on Veyra's **Keys** page, limited to the server's environment if you like. The secret is
shown once.

### 3. Choose the server's name

`SQLSERVER_NAME` is the name Veyra shows for the server, for example `sql-prod-01`. Veyra files the server's history
and alerts under it, so keep it once chosen. One Collector watches one server; run one per server, each with its own
name.

### 4. Encryption and the certificate

The configuration asks for an encrypted connection (`SQLSERVER_ENCRYPT=true`) and checks the server's certificate.

- **A certificate from your CA.** Nothing more to do; the Collector trusts what the machine trusts.
- **SQL Server's own self-signed certificate.** Set `SQLSERVER_TRUST_CERT=true`, which accepts the certificate
  without checking it. SQL Server's generated certificate also has a negative serial number, which the Collector
  refuses unless its environment has `GODEBUG=x509negativeserial=1`. Installing a proper certificate is better than
  either.
- `SQLSERVER_ENCRYPT=disable` turns encryption off. Use it only on a private network.

### 5a. Run the Collector on Windows, next to SQL Server

Install the contrib distribution (`otelcol-contrib`) from the
[Collector releases](https://github.com/open-telemetry/opentelemetry-collector-releases/releases) as a Windows
service, copy the configuration to its folder, and set the settings as system environment variables, from an
administrator PowerShell:

```powershell
Copy-Item integrations\sqlserver\collector.yaml "C:\Program Files\OpenTelemetry Collector\config.yaml"
[Environment]::SetEnvironmentVariable("VEYRA_OTLP_ENDPOINT", "https://veyra.example.com", "Machine")
[Environment]::SetEnvironmentVariable("VEYRA_INGESTION_KEY", "vyk_...", "Machine")
[Environment]::SetEnvironmentVariable("VEYRA_ENVIRONMENT", "production", "Machine")
[Environment]::SetEnvironmentVariable("SQLSERVER_NAME", "sql-prod-01", "Machine")
[Environment]::SetEnvironmentVariable("SQLSERVER_HOST", "127.0.0.1", "Machine")
[Environment]::SetEnvironmentVariable("SQLSERVER_PORT", "1433", "Machine")
[Environment]::SetEnvironmentVariable("SQLSERVER_USER", "veyra_monitor", "Machine")
[Environment]::SetEnvironmentVariable("SQLSERVER_PASSWORD", "...", "Machine")
Restart-Service otelcol-contrib
```

The key and the password are secrets: only administrators can read machine environment variables, but anyone who
can should be trusted with them.

### 5b. Or run it in a container

```bash
docker run -d --name veyra-sqlserver --restart unless-stopped \
  -v "$PWD/integrations/sqlserver/collector.yaml:/etc/otelcol-contrib/config.yaml:ro" \
  -e VEYRA_OTLP_ENDPOINT=https://veyra.example.com \
  -e VEYRA_ENVIRONMENT=production \
  -e SQLSERVER_NAME=sql-prod-01 \
  -e SQLSERVER_HOST=sql-prod-01.corp.local \
  -e SQLSERVER_PORT=1433 \
  -e SQLSERVER_USER=veyra_monitor \
  --env-file /etc/veyra-sqlserver.env \
  otel/opentelemetry-collector-contrib:0.140.0
```

`/etc/veyra-sqlserver.env` holds `VEYRA_INGESTION_KEY=vyk_...` and `SQLSERVER_PASSWORD=...`, so the secrets stay
out of the shell history. Keep it readable by root only (`chmod 600`).

A password with characters such as `@ : / ? # & %` must be URL-encoded in `SQLSERVER_PASSWORD`, because it becomes
part of a connection string; `p@ss` becomes `p%40ss`.

## What it sends

| Veyra shows | Metric or event | Every |
| --- | --- | --- |
| Batch requests per second | `sqlserver.batch.request.rate` | 15 s |
| Connections | `sqlserver.user.connection.count` | 15 s |
| Blocked sessions | `sqlserver.processes.blocked` | 15 s |
| Deadlocks | `sqlserver.deadlock.rate` | 15 s |
| Page life expectancy | `sqlserver.page.life_expectancy` | 15 s |
| Waits by category | `sqlserver.os.wait.duration` | 15 s |
| File I/O and latency per database | `sqlserver.database.io`, `.latency`, `.operations` | 15 s |
| Database size and growth per day | `sqlserver.database.size` (`sqlquery`) | 60 s |
| Time since the last full backup | `sqlserver.database.backup.age` (`sqlquery`) | 60 s |
| SQL Agent jobs: last run, how long, failed | `sqlserver.agent.job.last_run`, `.duration`, `.failed` (`sqlquery`) | 60 s |
| The longest running query per database | `sqlserver.query.duration.max` (`sqlquery`) | 60 s |
| Databases: state, settings, log use, virtual log files, files, volumes; log and differential backups, the log chain, last good CHECKDB, damaged pages | `sqlserver.database.offline`, `sqlserver.log.*`, `sqlserver.file.size`, `sqlserver.volume.*`, `sqlserver.database.*` (`sqlquery/storage`) | 5 min |
| Availability Groups: replicas, queues, lag, sync; log shipping; cluster nodes | `sqlserver.ag.*`, `sqlserver.logshipping.*`, `sqlserver.cluster.node.owner` (`sqlquery/server`) | 60 s |
| SQL Agent: each job running now, next run, late, usual duration; runs and steps with their messages | `sqlserver.agent.job.*`, `sqlserver.job` rows (`sqlquery/jobs`) | 60 s |
| The server: version, CPU of SQL Server and of others, runnable tasks, workers, memory, grants waiting, dumps, SQL Server Agent, restarts | `sqlserver.server.info`, `sqlserver.cpu.*`, `sqlserver.workers.ratio`, `sqlserver.memory.*`, `sqlserver.agent.stopped`, `sqlserver.server.*` (`sqlquery/server`) | 60 s |
| Indexes: missing, use, writes, size, keys, escalations; statistics age of large tables | `sqlserver.index` rows (`sqlquery/indexes`) | 1 h |
| Query Store: each plan per interval, with waits; whether each database keeps it | `sqlserver.querystore` rows (`sqlquery/querystore`) | 15 min |
| Deadlocks with their graphs, from `system_health` | `sqlserver.deadlock` rows (`sqlquery/events`) | 60 s |
| Errors of severity 20 and above, memory errors, long waits, non-yielding schedulers, security and connectivity records, from `system_health` | `sqlserver.event` rows (`sqlquery/events`) | 60 s |
| Plans of the 10 busiest statements, once per plan | `sqlserver.plan` rows (`sqlquery/plans`) | 15 min |
| The live view: busy sessions and blocking | `sqlserver.session` rows (`sqlquery/live`), kept as database activity | 10 s |
| Top queries over the range, per database | `sqlserver.statement.elapsed`, `.cpu`, `.executions`, `.logical_reads` (`sqlquery`) | 60 s |

- **Identity.** The `resource` processor adds `db.system.name=microsoft.sql_server` and `server.address` from
  `SQLSERVER_NAME` to everything, which makes the server one database block in Veyra, and each database a part of
  it. Telemetry from another Collector setup lands in the same block if it carries the same two attributes.
- **Rates.** Over a connection the receiver sends SQL Server's running totals for its "…/sec" counters, such as
  batch requests and deadlocks, under names ending in `.rate`. Veyra computes the rate from how the total changes, so
  deadlocks from before a window are never counted again.
- **Only the server.** On Windows the receiver also reads the local machine's performance counters, whatever server
  it is told to read. The `filter/connection-only` processor drops them, so the server's figures arrive once, and
  never another machine's.
- **Statements.** Each minute the `sqlquery` receiver reads the busiest 100 statements of the last two minutes from
  `sys.dm_exec_query_stats`, with their database, their text (up to 1,000 characters), and SQL Server's totals since
  the plan was cached. The `cumulativetodelta/statements` processor turns the totals into what each minute added; a
  plan cached after the agent started counts in full, so a query run once appears too. Veyra adds the minutes of the
  chosen range. Statements of Veyra's own agent are marked and kept out of the ranking. The receiver's own top-query
  events are off: they count a statement only from the second time they see it (VEY-43).
- **Statements and plans (ADR 0020).** The statement series carry `plan_hash` too, so each plan of a statement is
  measured on its own and a plan change shows. Every 15 minutes `sqlquery/plans` sends the plan XML of the 10
  busiest statements, up to 2 MB each; Veyra keeps a plan once a day, reads its operators and warnings (implicit
  conversions, spills, joins without a condition, missing indexes with a `CREATE INDEX` to copy), and offers it as
  a `.sqlplan` file.
- **Databases, backups, and integrity (ADR 0020).** Every 5 minutes `sqlquery/storage` reads each database's state
  and settings, its log's use and virtual log files (`sys.dm_db_log_stats`), every file with its growth and maximum,
  the volumes under them (`sys.dm_os_volume_stats`, no agent on the machine needed), the age of log and differential
  backups, gaps in the log chain since the last full backup, the last good CHECKDB, and damaged pages. The Databases
  tab says what needs doing, with the statement to run, and when each volume fills at the pace of the last week.
- **Errors and events (ADR 0020).** `sqlquery/events` also sends every other event `system_health` keeps in its
  current event file, each once: errors of severity 20 and above and errors 823 and 824, memory errors, lock waits
  over 30 seconds and latch waits over 15, non-yielding schedulers, and security and connectivity records. The Events
  tab words them. Errors below severity 20, such as 825 and failed SQL logins (18456), are only in SQL Server's error
  log. Reading it needs an agent on the server's own machine, and Veyra does not read it yet.
- **Availability (ADR 0020).** `sqlquery/server` also reads each Availability Group replica's role and health, each
  database's send and redo queues, lag, and synchronization, log shipping's delay against its own thresholds, and
  which cluster node runs the instance. A failover shows as a change of the primary within 15 minutes. Servers
  without them send nothing for them.
- **SQL Agent (ADR 0020).** Every minute `sqlquery/jobs` reads each enabled job's running time, next run, whether it
  is late, and its usual duration over its last 10 successful runs, and the runs and steps of the last 30 minutes with
  their messages, which Veyra keeps once each. The Jobs tab lists those that need a look in the Simple view and every
  job with its runs and steps of the last week in the Advanced view.
- **The server (ADR 0020).** Every minute `sqlquery/server` reads SQL Server's share of the CPU and everything else's
  from the scheduler monitor ring buffer, which Microsoft documents as unsupported but keeps stable (the CI check
  catches a change), runnable tasks, the share of worker threads in use, memory, grants waiting, memory dumps of the
  last day, whether SQL Server Agent runs, a restart in the last 15 minutes, and whether the version is past its end
  of extended support. The Server tab compares batch requests with the same hour of the week over the last four
  weeks, and the Waits tab explains the wait types with the most time.
- **Indexes and statistics (ADR 0020).** Every hour `sqlquery/indexes` reads the missing indexes SQL Server noted and,
  per database, the 300 largest indexes with their reads and writes since SQL Server started, size, keys, included
  columns, and lock escalations, and the statistics of tables over 100,000 rows. Veyra turns them into advice with
  the statement to run: indexes to add, unused or duplicate ones to drop, heaps, stale statistics, and escalations.
  Index fragmentation is not read: measuring it scans the indexes.
- **Query Store (ADR 0020).** Every 15 minutes `sqlquery/querystore` reads the intervals that ended in the last 20
  minutes from every database with Query Store on, in one batch that names each database, with each plan's runs,
  time, CPU, reads, and the wait category it waited on most. A statement's page shows this history beyond the plan
  cache, over restarts. A database without Query Store is listed with the statement that turns it on, for a DBA.
  The `db-plan-regression` rule warns when a statement runs many times slower on its newest plan than its best.
- **Blocking and deadlocks (ADR 0020).** Blocking episodes come from the live view: the same head blocker in reads
  at most 35 seconds apart is one episode. Deadlocks come from the current event file of SQL Server's built-in
  `system_health` session: every minute `sqlquery/events` sends those of the last five minutes with a hash of their
  graph, and Veyra keeps each once, for 90 days by default. The session's ring buffer returns at most 4 MB of XML,
  and on a server that has run for days the newest events are the ones it cuts off, so the agent reads it only where
  `system_health` writes no file. The session writes its file up to two minutes after an event, so a deadlock can
  reach Veyra that much later.
- **The live view (ADR 0020).** Every 10 seconds `sqlquery/live` reads each session that runs a request, blocks or
  waits for another, holds a transaction open, or holds more than 8 MB of tempdb, at most 200, with its wait,
  blocker, transaction age, tempdb use, memory grant, program, host, login, and statement, plus one row of
  server-wide figures. It sees a session that sleeps with an open transaction, the usual head of a blocking chain,
  which the receiver's query samples miss; they are off. Each row arrives as a log record with a `veyra_record`
  column, which Veyra keeps in its store for database activity instead of the Log Explorer, for 7 days by
  default (Settings → Data and retention). Veyra's own sessions are left out unless they block or wait.
- **Volume.** Around 300 series for a server with a few databases, plus four series for each statement that ranks
  among the busiest of a minute, and the live view's rows: a few hundred kilobytes a day for a quiet server, a few
  tens of megabytes for a busy one.

The `sqlquery` metric names are part of Veyra's contract: another setup that sends them under the same names and
units gets the same screen and rules.

## Built-in rules

Each rule has a switch and its levels on the **Settings** screen, under Alert rules.

| Rule | Caution | Warning | Clears |
| --- | --- | --- | --- |
| Blocking: sessions blocked for 5 minutes | 1 | 5 | none for 5 minutes |
| Deadlocks in the last 15 minutes | 1 | 5 | none for 30 minutes |
| Long query: the longest running query, per database | 5 min | 30 min | below caution for 5 minutes |
| Backup age: time since the last full backup, per database | 26 h | 50 h | a new backup |
| SQL Agent job: the last run failed, per job | failed | | the next run succeeds |
| Head blocker: the longest any request has waited behind another session | 1 min | 5 min | nothing waits |
| Open transaction: a session holding a transaction open without running anything | 10 min | 30 min | below caution |
| Session filling tempdb: the largest share of tempdb one session holds | 25 % | 50 % | below caution |
| Plan regression: times slower on the newest plan than the best, last hour | 3× | 10× | below caution |
| Database not online, per database | | not online | online |
| Log filling: share of the log in use, per database | 80 % | 95 % | below caution |
| Virtual log files, per database | 1,000 | 10,000 | below caution |
| Database volume space: share in use, per volume | 85 % | 95 % | below caution |
| Log backup age, per database in full or bulk-logged recovery | 1 h | 4 h | a new log backup |
| Broken log chain, per database | | a gap | a new full backup |
| Integrity check age: since the last good CHECKDB, per database | 8 days | 15 days | a good check |
| Damaged pages, per database | | 1 | repaired |
| SQL Server CPU | 80 % | 95 % | below caution |
| CPU taken by other processes | 25 % | 50 % | below caution |
| Worker threads in use | 80 % | 95 % | below caution |
| Memory grants waiting | 1 | 5 | none |
| Machine low on memory | | low | memory again |
| Memory dump in the last day | | 1 | none for a day |
| SQL Server Agent stopped | | stopped | running |
| Restart in the last 15 minutes | | restarted | 15 minutes up |
| Version out of support | | out of support | upgraded |
| Job running long: times its usual duration, per job, after 5 minutes | 2× | 4× | it finishes |
| Job did not run: 15 minutes after its scheduled time, per job | | late | it runs |
| Severe error: severity 20 and above, or 823/824, in 15 minutes | | 1 | none for 15 minutes |
| Memory error in 15 minutes | | 1 | none for 15 minutes |
| Failed logins and permission checks in 15 minutes | 10 | 50 | below caution |
| Replica not healthy, per replica | | not healthy | healthy |
| Replica falling behind: log to send or redo, per database | 100 MB | 1 GB | below caution |
| Database not synchronizing, per database | | not synchronizing | synchronizing |
| Failover, per availability group | | in 15 minutes | 15 steady minutes |
| Log shipping behind its threshold, per database | | behind | caught up |

The backup rule leaves `tempdb` alone, and every database that has never had a full backup. Databases that are not
backed up on purpose can be listed by name on the Settings screen.

## When a tab says Veyra cannot read it

Every 5 minutes the agent asks SQL Server which of the permissions above its login has (`HAS_PERMS_BY_NAME`) and
sends the answer as `sqlserver.login.permission.missing`: 1 for each permission the login lacks. A tab that needs a
missing permission says so at the top: what it cannot read, which permission is missing, and the statements that grant
it. Until then the tab does not claim that all is well, since an empty answer proves nothing.

A login created before Veyra read job steps, Query Store, indexes, and the configuration lacks some of the newer
grants. Run the statements the tab shows as an administrator; the notice goes away within 5 minutes.

## When Veyra is unreachable

The `otlphttp` exporter holds up to 1,000 batches in memory and retries each one for up to 10 minutes; a batch that
still fails after that is dropped, so an outage longer than 10 minutes leaves a gap. The memory limiter keeps the
Collector under 128 MiB.

## Check it

Within a minute, the server appears under Databases on the **Metrics** page. If it does not, the Collector's log
(`docker logs veyra-sqlserver`, or the Windows Event Log for the service) says why:

- `Login failed for user` for a wrong user or password, and `The SELECT permission was denied` for a missing grant
  from step 1.
- `x509: negative serial number` or `certificate signed by unknown authority` for the certificate (step 4).
- `401` from Veyra for a wrong key, and connection errors for a wrong endpoint.

On an idle server, such as one on a developer's machine, the Live and Blocking tabs stay quiet. To see them at work,
run [`samples/sqlserver-workload`](../../samples/sqlserver-workload/README.md) for a few minutes: a blocking chain, a
long query, and a deadlock each minute, all in a database of its own.

To check a change to the configuration, run the same check as CI, which needs Docker and Node.js:

```bash
bash integrations/sqlserver/test/run.sh
```
