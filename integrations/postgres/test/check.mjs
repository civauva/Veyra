// Checks what the Collector wrote while run.sh held a lock, ran a long query, and caused a deadlock: every metric Veyra
// reads arrives, under the block attributes Veyra files it by (ADR 0018), with the values the run set up.
//
// Usage: node check.mjs out/telemetry.jsonl [--describe]
// --describe also prints each metric's kind, unit, and attributes, as the guide and the header of collector.yaml
// list them.

import { readFileSync } from "node:fs";

const file = process.argv[2];
const describe = process.argv.includes("--describe");
const lines = readFileSync(file, "utf8").split("\n").filter(Boolean);

/** The highest value of each series over the run, keyed by name, database, table, and point attributes. */
const highest = new Map();

/** Every resource the metrics came with. */
const resources = [];

/** Each metric's kind, unit, and attribute names, as it arrived. */
const shapes = new Map();

const text = (value) => Object.values(value ?? {})[0];
const attributes = (list) => Object.fromEntries((list ?? []).map((one) => [one.key, text(one.value)]));

for (const line of lines) {
  const batch = JSON.parse(line);

  for (const resourceMetrics of batch.resourceMetrics ?? []) {
    const resource = attributes(resourceMetrics.resource.attributes);
    resources.push(resource);

    for (const scope of resourceMetrics.scopeMetrics) {
      for (const metric of scope.metrics) {
        const kind = metric.gauge ? "gauge" : metric.sum ? "sum" : "other";
        const points = (metric.gauge ?? metric.sum ?? { dataPoints: [] }).dataPoints;

        for (const point of points) {
          const own = attributes(point.attributes);
          // sqlquery names the database on the point, the receiver on the resource.
          const { "postgresql.database.name": pointDatabase, ...rest } = own;
          const database = pointDatabase ?? resource["postgresql.database.name"] ?? "";
          const parts = [database, resource["postgresql.table.name"] ?? "", ...Object.values(rest)].join("|");
          const key = `${metric.name}|${parts}`;
          const reading = Number(point.asDouble ?? point.asInt);
          highest.set(key, Math.max(highest.get(key) ?? -Infinity, reading));

          shapes.set(metric.name, {
            kind,
            monotonic: metric.sum?.isMonotonic === true,
            cumulative: metric.sum?.aggregationTemporality === 2,
            unit: metric.unit ?? "",
            resource: Object.keys(resource).filter((name) => name.startsWith("postgresql.")),
            point: Object.keys(own),
          });
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

/** The highest value of a metric for one database, table, and point attribute values. */
const most = (name, database = "", table = "", ...rest) => highest.get([name, database, table, ...rest].join("|"));

// Every metric the database screen and the rules read.
for (const name of [
  "postgresql.backends",
  "postgresql.connection.max",
  "postgresql.db_size",
  "postgresql.deadlocks",
  "postgresql.commits",
  "postgresql.rollbacks",
  "postgresql.rows",
  "postgresql.table.vacuum.count",
  "postgresql.database.locks",
  "postgresql.database.xid.age",
  "postgresql.sessions.blocked",
  "postgresql.query.duration.max",
]) {
  expect(shapes.has(name), `${name} never arrived`);
}

// Every resource names the server the way Veyra files it, and its environment.
expect(resources.length > 0, "no metrics arrived");
for (const resource of resources) {
  expect(resource["db.system.name"] === "postgresql", "a resource without db.system.name");
  expect(resource["server.address"] === "pg-check-01", "a resource without server.address");
  expect(resource["deployment.environment.name"] === "production", "a resource without an environment");
  expect(resource["service.name"] === undefined, "a resource with a service.name");
}

// The sqlquery metrics, under the names, units, and attributes of ADR 0018.
expect(shapes.get("postgresql.database.xid.age")?.unit === "{transactions}", "xid.age has the wrong unit");
expect(shapes.get("postgresql.sessions.blocked")?.unit === "{sessions}", "sessions.blocked has the wrong unit");
expect(shapes.get("postgresql.query.duration.max")?.unit === "s", "query.duration.max has the wrong unit");
expect(
  shapes.get("postgresql.database.xid.age")?.point.join() === "postgresql.database.name",
  "xid.age is not per database",
);
expect(
  shapes.get("postgresql.query.duration.max")?.point.join() === "postgresql.database.name",
  "query.duration.max is not per database",
);

// What run.sh set up.
expect(most("postgresql.database.xid.age", "shop") > 0, "shop has no transaction ID age");
const templates = [...highest.keys()].filter((key) => /\|template[01]\|/.test(key));
expect(templates.length === 0, `template databases were read: ${templates.join(", ")}`);
expect(most("postgresql.deadlocks", "shop") >= 1, "the deadlock in shop was not counted");
expect(most("postgresql.sessions.blocked") >= 1, "no session counted as blocked");
expect(most("postgresql.query.duration.max", "shop") >= 20, "the held lock in shop is not a long query");
expect(most("postgresql.query.duration.max", "reporting") >= 20, "the long report is not reporting's longest query");
expect(most("postgresql.query.duration.max", "postgres") < 20, "the Collector's own reads count as a long query");
expect(most("postgresql.db_size", "shop") > 0, "shop has no size");
expect(most("postgresql.backends", "shop") >= 1, "shop has no connections");
expect(most("postgresql.connection.max") >= 1, "the server has no connection limit");
expect(most("postgresql.rows", "shop", "public.orders", "dead") > 0, "orders has no dead rows");
expect(most("postgresql.rows", "shop", "public.orders", "live") >= 1000, "orders has not all its rows");
expect(most("postgresql.table.vacuum.count", "shop", "public.orders") >= 1, "the vacuum of orders was not counted");

if (describe) {
  for (const [name, shape] of [...shapes].sort(([a], [b]) => a.localeCompare(b))) {
    const sum = shape.kind === "sum" ? ` ${shape.cumulative ? "cumulative" : "delta"}` : "";
    const monotonic = shape.kind === "sum" ? (shape.monotonic ? " monotonic" : " not monotonic") : "";
    console.log(
      `${name}  ${shape.kind}${sum}${monotonic}  ${shape.unit}  resource: ${shape.resource.join(", ") || "-"}` +
        `  point: ${shape.point.join(", ") || "-"}`,
    );
  }
}

if (failures.length > 0) {
  console.error(`PostgreSQL check failed:\n- ${failures.join("\n- ")}`);
  process.exit(1);
}

console.log(`PostgreSQL check passed: ${shapes.size} metrics, ${lines.length} batches.`);
