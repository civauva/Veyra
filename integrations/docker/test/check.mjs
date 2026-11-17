// Checks what the Collector wrote while run.sh ran its containers: every container metric Veyra reads arrives with
// the unit and kind Veyra relies on, under the attributes Veyra files it by (ADR 0018), and each container's log
// lines arrive whole, with its name.
//
// Usage: node check.mjs out/telemetry.jsonl

import { readFileSync } from "node:fs";

const file = process.argv[2];
const lines = readFileSync(file, "utf8").split("\n").filter(Boolean);

const logger = "veyra-check-logger";
const restarter = "veyra-check-restarter";
const busy = "veyra-check-busy";

/**
 * The metrics Veyra's rules and screens read, with the unit and kind they must arrive with. Block I/O arrives only
 * once a container has used a disk, which a container running from cached layers may never do.
 */
const contract = {
  "container.cpu.utilization": { unit: "1", kind: "gauge" },
  "container.memory.usage.total": { unit: "By", kind: "sum" },
  "container.memory.usage.limit": { unit: "By", kind: "sum" },
  "container.memory.percent": { unit: "1", kind: "gauge" },
  "container.restarts": { unit: "{restarts}", kind: "cumulative" },
  "container.uptime": { unit: "s", kind: "gauge" },
  "container.network.io.usage.rx_bytes": { unit: "By", kind: "cumulative" },
  "container.blockio.io_service_bytes_recursive": { unit: "By", kind: "cumulative", sometimes: true },
};

/** Every point of a test container, with its metric's name, unit, and kind, and its resource. */
const points = [];

/** Every log record of a test container, with its resource, attributes, and text. */
const records = [];

/** Log records that name no container, though they came from one of the check's containers. */
let unnamed = 0;

const failures = [];
const expect = (condition, message) => {
  if (!condition) {
    failures.push(message);
  }
};

const text = (value) => Object.values(value ?? {})[0];
const attributes = (list) => Object.fromEntries((list ?? []).map((one) => [one.key, text(one.value)]));
const ours = (name) => [logger, restarter, busy].includes(name);

for (const line of lines) {
  const batch = JSON.parse(line);

  for (const resourceMetrics of batch.resourceMetrics ?? []) {
    const resource = attributes(resourceMetrics.resource.attributes);

    if (!ours(resource["container.name"])) {
      continue;
    }

    for (const scope of resourceMetrics.scopeMetrics) {
      for (const metric of scope.metrics) {
        const kind = metric.gauge
          ? "gauge"
          : metric.sum?.isMonotonic && metric.sum.aggregationTemporality === 2
            ? "cumulative"
            : "sum";

        for (const point of (metric.gauge ?? metric.sum).dataPoints) {
          points.push({
            name: metric.name,
            unit: metric.unit,
            kind,
            value: Number(point.asDouble ?? point.asInt),
            resource,
          });
        }
      }
    }
  }

  for (const resourceLogs of batch.resourceLogs ?? []) {
    const resource = attributes(resourceLogs.resource.attributes);

    for (const scope of resourceLogs.scopeLogs ?? []) {
      for (const record of scope.logRecords ?? []) {
        const body = text(record.body) ?? "";

        if (ours(resource["container.name"])) {
          records.push({ resource, attributes: attributes(record.attributes), body, time: record.timeUnixNano });
        } else if (/^(hello line|oops) \d+$/.test(body)) {
          unnamed++;
        }
      }
    }
  }
}

const of = (container, name) =>
  points.filter((point) => point.resource["container.name"] === container && point.name === name);
const anId = (resource) => /^[0-9a-f]{64}$/.test(resource["container.id"] ?? "");
const highest = (container, name) => Math.max(...of(container, name).map((point) => point.value));

// Every metric arrives for the containers, as the unit and kind Veyra reads it as.
for (const [name, { unit, kind, sometimes }] of Object.entries(contract)) {
  const all = points.filter((point) => point.name === name);

  expect(sometimes || of(logger, name).length > 0, `${name} never arrived for ${logger}`);
  expect(all.every((point) => point.unit === unit), `${name} is not in ${unit}`);
  expect(all.every((point) => point.kind === kind), `${name} is not a ${kind}`);
}

// Each series knows its container and its host, and is not a service (ADR 0018).
expect(points.length > 0, "no container metrics arrived");
expect(points.every((point) => anId(point.resource)), "a series has no container.id");
expect(points.every((point) => point.resource["container.image.name"]), "a series has no container.image.name");
expect(points.every((point) => point.resource["host.name"]), "a series has no host.name");
expect(points.every((point) => point.resource["deployment.environment.name"] === "check"),
  "the environment is not set");
expect(points.every((point) => point.resource["service.name"] === undefined), "a series has a service.name");

// The units hold what Veyra reads them as: CPU as a percent of one CPU, memory against the container's limit.
expect(highest(busy, "container.cpu.utilization") > 20, "a busy CPU did not read as a percent of one CPU");
expect(of(logger, "container.memory.usage.limit").every((point) => point.value === 64 * 1024 * 1024),
  "the memory limit is not the container's 64 MiB");
const usage = of(logger, "container.memory.usage.total").at(-1)?.value ?? 0;
const percent = of(logger, "container.memory.percent").at(-1)?.value ?? 0;
expect(Math.abs(percent - (usage / (64 * 1024 * 1024)) * 100) < 1,
  `memory percent ${percent} is not usage of the limit`);
expect(highest(restarter, "container.restarts") >= 1, "the restarting container counted no restarts");
expect(highest(logger, "container.restarts") === 0, "a container that never restarted counted restarts");
expect(highest(logger, "container.uptime") > 30, "the logger's uptime did not grow");

// Each container's lines arrive with its name, its ID, and its host, as plain text without the newline, stdout and
// stderr alike, and a line Docker split arrives whole.
const lines1 = records.filter((record) => record.resource["container.name"] === logger);
expect(lines1.some((record) => /^hello line \d+$/.test(record.body)), "the logger's stdout lines never arrived");
expect(lines1.some((record) => /^oops \d+$/.test(record.body) && record.attributes["log.iostream"] === "stderr"),
  "the logger's stderr lines never arrived");
expect(lines1.some((record) => record.body === "x".repeat(20000)), "the long line did not arrive whole");
expect(!lines1.some((record) => /^x+$/.test(record.body) && record.body.length < 20000),
  "a part of the long line arrived");
expect(records.some((record) => record.resource["container.name"] === restarter && record.body === "starting"),
  "the restarting container's lines never arrived");
expect(records.every((record) => anId(record.resource)), "a record has no container.id");
expect(records.every((record) => record.resource["host.name"]), "a record has no host.name");
expect(records.every((record) => record.resource["service.name"] === undefined), "a record has a service.name");
expect(records.every((record) => !record.body.endsWith("\n")), "a record still ends with its newline");
expect(records.every((record) => Number(record.time) > 0), "a record has no time");
expect(records.every((record) => Object.keys(record.attributes).every((key) => key === "log.iostream")),
  "a record carries more than its stream");
expect(unnamed === 0, `${unnamed} of the check's log lines arrived without a container name`);

if (failures.length > 0) {
  console.error(`Docker check failed:\n- ${failures.join("\n- ")}`);
  process.exit(1);
}

console.log(`Docker check passed: ${points.length} points, ${records.length} log records, ${lines.length} batches.`);
