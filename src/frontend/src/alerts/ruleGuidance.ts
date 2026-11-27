import type { AlertDetails, FixStep } from "../api/types";
import { catalogGuidance, isCatalogRule } from "./ruleCatalog";
import { containerName, serverName } from "./ruleWords";

/*
 * What an alert means and how to resolve it (VEY-60), for its details. Catalog rules bring theirs from the API, with
 * {server} and {part} to fill in; the rules of ADR 0012 and ADR 0018 have theirs here, beside their other words. The
 * scripts are for a person who understands them, to run where the steps say; Veyra never runs them.
 */

/** What an alert means and what to do about it. */
export interface Guidance {
  /** What is wrong and why it matters, in a sentence or two. */
  meaning: string;
  /** What to do, in order, with any script filled in. */
  steps: FixStep[];
}

/** What a guidance needs of an alert: its rule and what it is about. */
type GuidedAlert = Pick<AlertDetails, "rule" | "block">;

/**
 * What an alert means and how to resolve it.
 *
 * @param alert - The alert.
 * @returns The guidance, or null for a catalog rule until the catalog has answered.
 */
export function ruleGuidance(alert: GuidedAlert): Guidance | null {
  const { rule, block } = alert;

  if (isCatalogRule(rule)) {
    const entry = catalogGuidance(rule);

    return entry ? fill(entry.meaning, entry.steps, serverName(block.name), block.part ?? "") : null;
  }

  const name = block.name;
  const part = block.part ?? "";
  const server = serverName(name);
  const postgres = name.startsWith("postgresql ");

  switch (rule) {
    case "error-rate":
      return {
        meaning:
          `${name} logs a large share of errors among its log records. Something in it, or something it depends ` +
          "on, is failing for its users.",
        steps: [
          step(
            "Open its error logs and find the message that repeats most; the most frequent errors are listed in the " +
              "Advanced view.",
          ),
          step(
            "Open a failed trace to see which call fails first: the service itself, its database, or a service it calls.",
          ),
          step(
            "If it began with a release, roll it back or fix it forward. If a dependency fails, look at that one's " +
              "alerts.",
          ),
        ],
      };
    case "latency-p95":
      return {
        meaning:
          `One in twenty of ${name}'s requests takes longer than the threshold. Users find the application slow, and ` +
          "slow requests can pile up until it times out.",
        steps: [
          step(
            "Open its slow traces and see which span takes the time: a database query, a call to another service, or its own code.",
          ),
          step("Compare the slowest routes in the Advanced view: is it one route or all of them?"),
          step(
            "One slow query: add its index or look at its plan on the database server's screen. Every route slow: " +
              "look at the host's CPU and memory and at the database's load.",
          ),
        ],
      };
    case "disk-space":
      return {
        meaning:
          `Disk ${part} on ${name} is nearly full. When it is full, programs cannot write: databases stop, logs are ` +
          "lost, and services can crash.",
        steps: diskSteps(part),
      };
    case "disk-filling":
      return {
        meaning:
          `At the pace of its last day, disk ${part} on ${name} is full within the threshold. Nothing has failed ` +
          "yet; this is the time to act.",
        steps: [
          step(
            "See when it started to grow on the chart: log files, database files, and backups are the usual causes.",
          ),
          ...diskSteps(part),
        ],
      };
    case "container-restarts":
      return {
        meaning:
          `${containerName(block)} keeps restarting: it crashes or is killed, and Docker starts it again. While it ` +
          "restarts, what it serves is unavailable.",
        steps: [
          step(
            `Read its last log lines before the restart, on ${part || "its host"}.`,
            `docker logs --tail 100 ${name}`,
          ),
          step(
            "See why it stopped. Exit code 137 or OOMKilled true means it was killed for memory; other codes are the " +
              "program's own error.",
            `docker inspect --format '{{.State.ExitCode}} {{.State.OOMKilled}} {{.State.Error}}' ${name}`,
          ),
          step(
            "Fix the cause: its configuration, a dependency it cannot reach when it starts, or too low a memory limit.",
          ),
        ],
      };
    case "container-memory":
      return {
        meaning:
          `${containerName(block)} uses most of its memory limit. At the limit the kernel kills it, and it ` +
          "restarts.",
        steps: [
          step(`See its memory now, on ${part || "its host"}.`, `docker stats --no-stream ${name}`),
          step(
            "If its memory grows and never falls, it leaks: restart it to buy time and report it to its owners. If " +
              "it needs the memory, raise its limit (mem_limit in its compose file).",
          ),
        ],
      };
    case "db-blocking":
      return {
        meaning:
          `Sessions on ${server} wait for locks another session holds. Users see the application hang or time out ` +
          "for as long as it lasts.",
        steps: postgres
          ? [
              step(
                "Find the waiting sessions and the sessions they wait for.",
                "SELECT pid, pg_blocking_pids(pid) AS blocked_by, wait_event_type, now() - query_start AS waiting,\n" +
                  "       usename, application_name, query\n" +
                  "FROM pg_stat_activity\n" +
                  "WHERE cardinality(pg_blocking_pids(pid)) > 0;",
              ),
              step(
                "Ask the blocking session's owner to commit or roll back. As a last resort, end it; its work rolls back.",
                "SELECT pg_terminate_backend(<pid>);",
              ),
              step(
                "To keep it from coming back, keep transactions short and add the index the waiting statements need.",
              ),
            ]
          : [
              step(
                "Find the blocked requests and the sessions they wait for, on the server's Blocking tab or with this query.",
                "SELECT r.session_id, r.blocking_session_id, r.wait_type, r.wait_time / 1000 AS wait_seconds,\n" +
                  "       s.login_name, s.host_name, s.program_name, t.text\n" +
                  "FROM sys.dm_exec_requests AS r\n" +
                  "JOIN sys.dm_exec_sessions AS s ON s.session_id = r.session_id\n" +
                  "CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) AS t\n" +
                  "WHERE r.blocking_session_id <> 0;",
              ),
              step(
                "Ask the blocking session's owner to commit or roll back. As a last resort end it with KILL and its " +
                  "session ID; its work rolls back.",
              ),
              step(
                "To keep it from coming back, keep transactions short, add the index the blocked statements need, or " +
                  "let readers use read committed snapshot isolation.",
              ),
            ],
      };
    case "db-deadlocks":
      return {
        meaning:
          `Sessions on ${server} waited for each other's locks, and the server ended one of them so that the others ` +
          "could go on. The ended statement failed with an error in its application.",
        steps: [
          postgres
            ? step("Read the server's log: PostgreSQL logs each deadlock with the statements involved.")
            : step(
                "Open the deadlock on the server's Blocking tab: it shows the statements and what they fought over.",
              ),
          step(
            "Make the statements use tables in the same order, keep transactions short, and add the index that " +
              "lets them lock fewer rows.",
          ),
          step(
            postgres
              ? "Let the application retry a statement that failed with SQLSTATE 40P01."
              : "Let the application retry a statement chosen as the deadlock victim, error 1205.",
          ),
        ],
      };
    case "db-long-query":
      return {
        meaning:
          `A query in ${part || "a database"} on ${server} has run for a long time. It may hold locks, use much of ` +
          "the CPU or disk, or wait for something itself.",
        steps: postgres
          ? [
              step(
                "See it, how long it has run, and what it waits for.",
                "SELECT pid, now() - query_start AS running, state, wait_event_type, usename, query\n" +
                  "FROM pg_stat_activity\n" +
                  "WHERE state <> 'idle'\n" +
                  "ORDER BY query_start;",
              ),
              step("If it should not run that long, ask its owner, or cancel it.", "SELECT pg_cancel_backend(<pid>);"),
              step("If it is expected, such as a report or maintenance, run it outside busy hours."),
            ]
          : [
              step("See it on the server's Live tab, with what it waits for and its plan."),
              step(
                "If it should not run that long, ask its owner, or end it; its work rolls back.",
                "KILL <session_id>;",
              ),
              step("If it is expected, such as a report or maintenance, run it outside busy hours."),
            ],
      };
    case "db-backup-age":
      return {
        meaning:
          `${part || "A database"} on ${server} has had no full backup for a while. If its disk or the server fails, ` +
          "everything since its last backup is lost.",
        steps: [
          step(
            "Check the SQL Server Agent job that backs it up: is it enabled, did it fail, does Agent run? The " +
              "server's Backups and Agent tabs show it.",
          ),
          step(
            "Take a full backup now.",
            `BACKUP DATABASE ${bracketed(part)} TO DISK = N'<backup folder>\\${quoted(part)}.bak' WITH CHECKSUM;`,
          ),
          step("Keep the backups on another machine as well, and restore one now and then to know that they work."),
        ],
      };
    case "db-agent-job":
      return {
        meaning:
          `The last run of SQL Agent job ${part} on ${server} failed. Whatever it does, such as a backup or ` +
          "maintenance, did not happen.",
        steps: [
          step(
            "See the error of the step that failed.",
            "SELECT TOP (10) h.run_date, h.run_time, h.step_id, h.step_name, h.message\n" +
              "FROM msdb.dbo.sysjobhistory AS h\n" +
              "JOIN msdb.dbo.sysjobs AS j ON j.job_id = h.job_id\n" +
              `WHERE j.name = N'${quoted(part)}' AND h.run_status = 0\n` +
              "ORDER BY h.instance_id DESC;",
          ),
          step(
            "Fix the cause, such as a missing folder, a permission of the Agent's account, or a renamed database, then " +
              "run it again.",
            `EXEC msdb.dbo.sp_start_job @job_name = N'${quoted(part)}';`,
          ),
        ],
      };
    case "db-connections":
      return {
        meaning:
          `${server} uses most of its maximum connections. When none is left, new connections are refused and ` +
          "applications fail to connect.",
        steps: [
          step(
            "See who holds the connections.",
            "SELECT usename, application_name, client_addr, state, count(*) AS connections\n" +
              "FROM pg_stat_activity\n" +
              "GROUP BY usename, application_name, client_addr, state\n" +
              "ORDER BY connections DESC;",
          ),
          step(
            "Many idle connections: lower the applications' pool sizes, or put a pooler such as PgBouncer in front. " +
              "Idle in transaction: fix the application that leaves its transactions open.",
          ),
          step("Raise max_connections only with the memory to match; it takes a restart."),
        ],
      };
    case "db-wraparound":
      return {
        meaning:
          `The oldest transaction ID in ${part || "a database"} on ${server} is getting close to the wraparound ` +
          "limit. Near it, PostgreSQL stops accepting writes, to protect the data, until a vacuum freezes old rows.",
        steps: [
          step(
            "In the database, find the tables with the oldest rows.",
            "SELECT relname, age(relfrozenxid) AS xid_age\n" +
              "FROM pg_class\n" +
              "WHERE relkind = 'r'\n" +
              "ORDER BY age(relfrozenxid) DESC\n" +
              "LIMIT 10;",
          ),
          step(
            "Find what keeps autovacuum from freezing them: a long open transaction or an abandoned replication slot.",
            "SELECT pid, now() - xact_start AS open_for, state, query\n" +
              "FROM pg_stat_activity\n" +
              "WHERE xact_start IS NOT NULL\n" +
              "ORDER BY xact_start\n" +
              "LIMIT 5;\n" +
              "SELECT slot_name, active, age(xmin) AS xmin_age FROM pg_replication_slots;",
          ),
          step("Once nothing holds it back, vacuum the oldest tables.", "VACUUM (FREEZE, VERBOSE) <table>;"),
        ],
      };
  }

  return null;
}

/** What to do about a full or filling disk, with the command that finds what takes the room on Linux. */
function diskSteps(mountpoint: string): FixStep[] {
  const windows = /^[A-Za-z]:/.test(mountpoint);

  return [
    windows
      ? step("Find what takes the room, with Windows' Storage settings or a tool such as WinDirStat.")
      : step(
          "Find what takes the room.",
          `sudo du -xh ${mountpoint || "/"} --max-depth=2 2>/dev/null | sort -h | tail -20`,
        ),
    step(
      "Remove what is not needed: old logs, old backups, unused Docker images (docker image prune), package caches.",
    ),
    step("If the data has to stay, make the disk larger, or move some of it to another disk."),
  ];
}

/** A step, with or without a script. */
function step(text: string, script: string | null = null): FixStep {
  return { text, script };
}

/** Fills a catalog rule's guidance with the server and its part. */
function fill(meaning: string, steps: FixStep[], server: string, part: string): Guidance {
  const words = (text: string) => text.replaceAll("{server}", server).replaceAll("{part}", part);

  return {
    meaning: words(meaning),
    steps: steps.map((one) => ({
      text: words(one.text),
      script:
        one.script === null
          ? null
          : one.script
              .replaceAll("[{part}]", bracketed(part))
              .replaceAll("{part}", quoted(part))
              .replaceAll("{server}", server),
    })),
  };
}

/** A SQL Server name in brackets, as T-SQL quotes it: `[Sales]`, with any `]` doubled. */
function bracketed(name: string): string {
  return `[${name.replaceAll("]", "]]")}]`;
}

/** A name safe inside `N'…'`, with any `'` doubled. */
function quoted(name: string): string {
  return name.replaceAll("'", "''");
}
