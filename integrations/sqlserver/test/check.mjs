// Checks what the Collector wrote while run.sh held a lock: every metric and event Veyra reads arrives, under the
// block attributes Veyra files it by (ADR 0018), with the values the seed set up.
//
// Usage: node check.mjs out/telemetry.jsonl

import { readFileSync } from "node:fs";

const file = process.argv[2];
const lines = readFileSync(file, "utf8").split("\n").filter(Boolean);

/** The latest value of each metric, keyed by name and its point attributes. */
const latest = new Map();

/** The highest value of each metric over the run, for states that come and go, such as a blocked session. */
const highest = new Map();

/** Every resource the metrics came with. */
const resources = [];

/** The event names of the log records. */
const events = new Set();

/** Log records whose attributes still carry a query plan. */
let plans = 0;

/** The rows of the live view's sessions query (ADR 0020), with their attributes and body. */
const sessions = [];
const kinds = new Map();

/** Every point of the statement metrics, with its attributes and whether it is a delta (VEY-43). */
const statements = [];

const text = (value) => Object.values(value ?? {})[0];
const attributes = (list) => Object.fromEntries((list ?? []).map((one) => [one.key, text(one.value)]));

for (const line of lines) {
  const batch = JSON.parse(line);

  for (const resourceMetrics of batch.resourceMetrics ?? []) {
    resources.push(attributes(resourceMetrics.resource.attributes));

    for (const scope of resourceMetrics.scopeMetrics) {
      for (const metric of scope.metrics) {
        const points = (metric.gauge ?? metric.sum ?? { dataPoints: [] }).dataPoints;

        for (const point of points) {
          const parts = Object.values(attributes(point.attributes)).join(",");
          const reading = Number(point.asDouble ?? point.asInt);
          latest.set(`${metric.name}|${parts}`, reading);
          highest.set(`${metric.name}|${parts}`, Math.max(highest.get(`${metric.name}|${parts}`) ?? -Infinity, reading));

          if (metric.name.startsWith("sqlserver.statement.")) {
            statements.push({
              name: metric.name,
              delta: metric.sum?.aggregationTemporality === 1,
              value: Number(point.asDouble ?? point.asInt),
              ...attributes(point.attributes),
            });
          }
        }
      }
    }
  }

  for (const resourceLogs of batch.resourceLogs ?? []) {
    for (const scope of resourceLogs.scopeLogs ?? []) {
      for (const record of scope.logRecords ?? []) {
        events.add(record.eventName);

        const row = attributes(record.attributes);

        if (row.veyra_record !== undefined) {
          kinds.set(row.veyra_record, (kinds.get(row.veyra_record) ?? 0) + 1);
        }

        if (row.veyra_record === "sqlserver.session") {
          sessions.push({ ...row, text: text(record.body) ?? "" });
        }

        if (attributes(record.attributes)["sqlserver.query_plan"] !== undefined) {
          plans += 1;
        }
      }
    }
  }
}

const failures = [];
const expect = (ok, message) => {
  if (!ok) {
    failures.push(message);
  }
};
const value = (name, parts = "") => latest.get(`${name}|${parts}`);
const names = new Set([...latest.keys()].map((key) => key.split("|")[0]));

// Every metric the database screen and the rules read.
for (const name of [
  "sqlserver.batch.request.rate",
  "sqlserver.user.connection.count",
  "sqlserver.processes.blocked",
  "sqlserver.deadlock.rate",
  "sqlserver.page.life_expectancy",
  "sqlserver.os.wait.duration",
  "sqlserver.database.latency",
  "sqlserver.database.operations",
  "sqlserver.database.size",
  "sqlserver.database.backup.age",
  "sqlserver.agent.job.failed",
  "sqlserver.agent.job.last_run",
  "sqlserver.agent.job.duration",
  "sqlserver.query.duration.max",
  "sqlserver.statement.elapsed",
  "sqlserver.statement.cpu",
  "sqlserver.statement.executions",
  "sqlserver.statement.logical_reads",
]) {
  expect(names.has(name), `${name} never arrived`);
}

// Every resource names the server the way Veyra files it, and its environment.
expect(resources.length > 0, "no metrics arrived");
for (const resource of resources) {
  expect(resource["db.system.name"] === "microsoft.sql_server", "a resource without db.system.name");
  expect(resource["server.address"] === "sql-check-01", "a resource without server.address");
  expect(resource["deployment.environment.name"] === "production", "a resource without an environment");
}

// What the seed set up.
expect(value("sqlserver.agent.job.failed", "Nightly full backup - reporting") === 1, "the failing job is not failed");
expect(value("sqlserver.agent.job.failed", "Purge sessions") === 0, "the succeeding job is not succeeded");
expect(value("sqlserver.database.backup.age", "shop") >= 0, "shop has no backup age");
expect(value("sqlserver.database.backup.age", "reporting") === undefined, "reporting was never backed up");
expect(value("sqlserver.database.size", "shop,data") > 0, "shop has no data size");
// run.sh fires a deadlock while the Collector reads; its graph must arrive (ADR 0020, area 3).
const received = [...kinds].map(([kind, count]) => `${kind} ${count}`).join(", ");
expect(kinds.has("sqlserver.deadlock"), `no deadlock record arrived; records: ${received}`);

// The check's login has every grant of the SQL Server guide, so the agent reports none missing (VEY-58).
const permissions = [...latest].filter(([key]) => key.startsWith("sqlserver.login.permission.missing|"));
expect(permissions.length === 15, `the agent reported ${permissions.length} permissions, not 15`);
for (const [key, missing] of permissions) {
  expect(missing === 0, `${key.split("|")[1]} is missing`);
}
// master and model are dated 2003 on every server; a database never checked counts from the installation instead.
for (const [key, age] of latest) {
  if (key.startsWith("sqlserver.database.checkdb.age|") || key.startsWith("sqlserver.database.log_backup.age|")) {
    expect(age < 10 * 365 * 86400, `${key} counts from before the installation: ${age} s`);
  }
}
expect((highest.get("sqlserver.query.duration.max|shop") ?? 0) >= 20, "the blocked update is not the longest query");
expect(Math.max(...[...highest].filter(([key]) => key.startsWith("sqlserver.processes.blocked|")).map(([, v]) => v)) >= 1,
  "no session counted as blocked");

// The receiver's own query events are off: the sessions query replaces its samples, the statements its top queries.
expect(!events.has("db.server.query_sample"), "the receiver's query samples are still on");
expect(!events.has("db.server.top_query"), "the receiver's top queries are still on");

// The live view: a read every 10 seconds, each with its server row; the sleeping connection that holds a transaction
// open heads a chain, and the update waits for it; the agent's own sessions are left out.
const reads = new Map();
for (const row of sessions) {
  reads.set(row.sample, [...(reads.get(row.sample) ?? []), row]);
}
expect(reads.size >= 5, `only ${reads.size} reads of the live view arrived`);
expect([...reads.values()].every((rows) => rows.some((row) => row.session_id === "0")), "a read without its server row");
expect(sessions.some((row) => row.session_id === "0" && Number(row.tempdb_size_kb) > 0), "tempdb's size never arrived");
const sleeper = sessions.find(
  (row) => row.running === "0" && row.blocking === "1" && Number(row.transaction_ms) > 0 && row.text.includes("Id = 2"),
);
expect(sleeper !== undefined, "the sleeping connection with an open transaction never headed a chain");
expect(
  sleeper === undefined || sessions.some((row) => row.blocked_by === sleeper.session_id && row.wait_type.startsWith("LCK_M_")),
  "no update waited for the sleeping connection",
);
expect(!sessions.some((row) => row.program === "veyra-collector" && row.blocking === "0" && row.blocked_by === "0"),
  "the agent's own sessions were sent");
expect(plans === 0, `${plans} log records still carry a query plan`);

// The statements, as what each minute added: the query run once arrives counted in full, in its database, as the
// application's; the agent's own statements are marked as its own.
expect(statements.length > 0 && statements.every((point) => point.delta), "statement metrics are not deltas");
const oneOff = statements.filter(
  (point) => point.name === "sqlserver.statement.executions" && (point.query_text ?? "").includes("TOP (2000) object_id"),
);
expect(oneOff.reduce((sum, point) => sum + point.value, 0) >= 1, "the query run once was not counted");
expect(oneOff.every((point) => point.database_name === "shop" && point.agent === "false"), "the query run once is not shop's");
expect(statements.some((point) => point.agent === "true"), "no statement is marked as the agent's own");

if (failures.length > 0) {
  console.error(`SQL Server check failed:\n- ${failures.join("\n- ")}`);
  process.exit(1);
}

console.log(
  `SQL Server check passed: ${names.size} metrics, ${reads.size} live reads, ${lines.length} batches.`,
);
