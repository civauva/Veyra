# 0023. The Overview's figures: ingest counts, availability, jobs, and databases across servers

Date: 2026-11-26
Status: Accepted

## Context

The Console look's Overview (VEY-69, reference screenshot 5) shows the whole environment on one screen:

- figures: services healthy, hosts online, database instances, request volume, error rate, open alerts, jobs
  succeeded, and availability over 30 days
- panels: needs attention, request activity, and telemetry ingestion by signal
- tables: applications, infrastructure, and the SQL Server databases of every server
- below them: pipeline executions, host resource trends, and collector health

Most of this exists. ADR 0022 gave the resource lists, their headers, and their comparisons, and the home snapshot
(ADR 0007) has the alerts in need. Four things do not exist:

- **Telemetry ingestion by signal over time.** Only the log counts are kept per minute (ADR 0015). Spans and metric
  points are only counted live, in memory (ADR 0022).
- **Availability.** Nothing works out how long services were up.
- **Jobs across servers.** SQL Server Agent job runs are read one server at a time.
- **Databases across servers.** Databases are listed one server at a time.

Every figure is worked out on the server and answered within the speed budgets of ADR 0015.

## Decision

1. **One read for the Overview.** `GET /api/v1/overview?environment=&from=&to=` answers:
   - the header figures, each with its change against the period before where it changes over time
   - the request activity: requests and server errors per step, across services with HTTP metrics
   - the ingestion: log records, spans, and metric points per step
   - the host trends: average CPU, average memory, and the fullest disk per step
   - the pipeline runs: the latest run of each job, failed ones first
   - the collector health: each agent with when it was last seen and its state
   - the databases across servers, worst first, as one page

   The applications and infrastructure tables reuse the resource lists of ADR 0022, worst first, limited to the rows
   that fit. Needs attention reuses the home snapshot's alerts. Nothing is worked out twice.

2. **Ingest counts per minute, by signal.**
   - **Where they are kept.** `storage.ingest_count_minute` keeps, per minute, environment, and signal (logs, spans,
     points), how many arrived.
   - **When they are written.** Each writer adds its batch's counts in the same transaction as the batch, as the log
     counts do (ADR 0015).
   - **Coarser steps.** Steps of an hour or more add up the minutes. The table is small enough: three signals,
     times the environments, times 1,440 minutes a day.
   - **How long they are kept.** Daily partitions are dropped after 30 days, the longest range a screen offers.

3. **Availability.**
   - **What it is.** Over the last 30 days, the share of time in which no service had an open Warning alert, read
     from the alert history (ADR 0012).
   - **What it is not.** It is not an uptime probe; Veyra does not probe services. The tile's question mark says so.
   - **The change.** It is compared with the 30 days before.
   - **When it is unknown.** With no services, it is null.

4. **Jobs succeeded.**
   - **What it counts.** Every SQL Server Agent job run (ADR 0020, area 10) of every server in the window, without
     SQL Server's own system jobs as the Jobs tab leaves them out (VEY-66): succeeded runs out of all runs, and how
     many failed.
   - **More sources later.** Pipelines from other sources join the same figure when job runs from telemetry exist
     (VEY-69 step 6).

5. **Collector health.** Each agent comes with its state, when it was last seen, and its version. Queue length is
   left out until agents report their own exporter queue. The table says so rather than showing an invented number.

6. **Databases across servers.**
   - **What the list holds.** One page of every database of every server: name, server, engine, state, size, growth
     per day, the age of the last full backup, and connections where the engine reports them per database.
   - **Order.** Worst first: a late backup or a state other than online first.
   - **System databases.** They are left out unless asked for (VEY-66).

## Alternatives considered

- **Count spans and metric points from their tables when read.** It needs no new table, but counting raw points over
  30 days misses the speed budget by orders of magnitude.
- **Availability from an uptime probe.** It is truer for public endpoints, but Veyra does not probe, and doing so would
  need a new component and credentials. It may come later as its own decision.
- **Each Overview panel calls its own endpoint.** It is simpler on the server, but the page would make ten requests on
  every refresh, and figures would come from slightly different moments.

## Consequences

- The writers add a counting insert per batch, which is the same cost the log writer already pays.
- The Overview loads in one request and refreshes on the shell's tick.
- Availability means "no open warning", and its help says exactly that.
- Queue length shows as not reported until agents send it.
