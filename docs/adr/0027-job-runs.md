# 0027. Job runs: recognised from telemetry, SQL Server Agent first

Date: 2026-11-26
Status: Accepted

## Context

The Console look has a Jobs & pipelines entry (VEY-69 step 6). Reference screenshot 5 shows pipeline executions with
the last run, duration, status, and success share. Screenshot 7 shows a run's records processed and retries.

Today only SQL Server Agent jobs are known (ADR 0020):

- **How runs are kept.** As `sqlserver.job` activity records for 90 days.
- **Where they show.** On a server's Jobs tab, and on the Overview (ADR 0023), which promised that other pipelines
  join the same figure.

Applications run jobs too: Hangfire, Quartz.NET, hosted services, ETL workers, and CI/CD pipelines.

- **They are traced already.** They send spans through the standard OpenTelemetry SDKs, and Veyra requires no SDK of
  its own (AGENTS.md).
- **Nothing reads them as jobs.** A job's run is a root span; nothing reads it as one.
- **Nothing keeps them long enough.** Spans are kept 7 days (ADR 0014), too short for a month's success share.

## Decision

### 1. What a job run is

- **The fields.** A run has:
  - a source (`trace` or `sqlagent`) and the block it ran on (a service, or a database server)
  - the job's name and the run's ID
  - when it started, how long it took, and its status: succeeded, failed, or running
  - its steps, its records processed and expected, its retries, and its trace and message
- **The job's identity.** A job is its source, block, environment, and name.

### 2. Runs from traces

- **Which root spans count.** A root span is a job run when it carries a job name attribute. The attributes are a
  setting, `Jobs:NameAttributes`, on the Settings screen. By default:
  - `cicd.pipeline.name`, the OpenTelemetry CI/CD convention
  - `job.name`, which Quartz.NET, Hangfire integrations, and most hand-made instrumentation use
- **The run's ID.** `cicd.pipeline.run.id` when present, otherwise the trace ID.
- **Status.** Error status means failed, and anything else succeeded.
- **Steps.** The root's direct children, in order, with their name, duration, and status.
- **Records and retries.** Read when the root span carries them:
  - `records.processed`, or `job.records.processed`
  - `records.total`, or `job.records.total`
  - `retry.count`, or `job.retry.count`

  These names are documented for applications that want them. Nothing else is required.

### 3. Where runs are kept

- **The table.** `storage.job_run`, partitioned by day and kept 30 days (`JobRunRetentionDays`), the longest range a
  screen offers.
- **Traced runs.** The span writer adds them in the same transaction as the spans, as counts are added (ADR 0015).
- **SQL Server Agent runs.** The activity writer adds them when a run record (`step_id = 0`) arrives. Their steps stay
  in the activity records, which are kept 90 days, and are read from there.
- **Why a table of its own.** Reading runs back from spans would expire with the spans after 7 days, and would scan
  JSON at read time.

### 4. The API

- **`GET /api/v1/jobs?from=&to=&environment=&source=&q=&status=`.**
  - One row per job, failing ones first and then by name.
  - Each row: the last run, the runs and failures of the window, the success share, the usual duration, the
    statuses of the latest 20 runs, and whether it runs now.
- **`GET /api/v1/jobs/{id}?from=&to=`.** One job: its runs of the window, newest first, each with steps, records,
  retries, trace, and message, and its figures.
- **The Overview.** Its pipeline panel and jobs figure (ADR 0023) read the same table, so traced jobs join them.
- **Incidents.** An incident's execution path (ADR 0026) reads it for a failed job.

### 5. The screens

- **The routes.** One registry: `jobs` at `/jobs`, under Resources, and `job` at `/jobs/:id`.
- **Console.** It draws them with Console's figures, tables, and run strips.
- **Every other look.** It gets a plain list and screen from the same feature.

## Alternatives considered

- **A Veyra attribute such as `veyra.job`.** It would be simpler to read, but applications would have to add a
  Veyra-specific attribute, which AGENTS.md rules out.
- **Runs from log records.** Logs mark runs less reliably than a root span: there is no duration and no steps. They
  can join later with their own rule.
- **Read runs from spans when asked.** It needs no new table, but the history would be capped at 7 days, and reading
  would scan JSON.
- **Missed runs from schedules.** A late SQL Agent job is already known. For traced jobs, schedules are not in the
  telemetry; that stays for a later decision (monitoring scope).

## Consequences

- The span writer reads the root spans' attributes for the job names, a cheap check per root span.
- Traced jobs keep 30 days of runs even when their spans are gone; their trace link then says the trace has expired.
- The setting lets an installation add its own job attribute without a release.
