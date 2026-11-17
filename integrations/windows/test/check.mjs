// Checks what the Collector wrote with integrations/windows: the host's metrics under host.name, Event Log errors and
// nothing milder, and IIS requests as logs of their site's service with Veyra's attribute names (VEY-35, ADR 0018).
//
//   node check.mjs out/telemetry.jsonl [--iis] [--own-event]
import { readFileSync } from "node:fs";

const [path, ...flags] = process.argv.slice(2);
const iis = flags.includes("--iis");
const ownEvent = flags.includes("--own-event");
const batches = readFileSync(path, "utf8").split("\n").filter(Boolean).map((line) => JSON.parse(line));

const attributes = (list) =>
  Object.fromEntries(
    (list ?? []).map(({ key, value }) => [key, value.stringValue ?? value.intValue ?? value.doubleValue ?? value.boolValue]),
  );

const metrics = new Set();
const hostResources = [];
const events = [];
const requests = [];

for (const batch of batches) {
  for (const resourceMetrics of batch.resourceMetrics ?? []) {
    hostResources.push(attributes(resourceMetrics.resource?.attributes));

    for (const scope of resourceMetrics.scopeMetrics ?? []) {
      for (const metric of scope.metrics ?? []) {
        metrics.add(metric.name);
      }
    }
  }

  for (const resourceLogs of batch.resourceLogs ?? []) {
    const resource = attributes(resourceLogs.resource?.attributes);

    for (const scope of resourceLogs.scopeLogs ?? []) {
      for (const record of scope.logRecords ?? []) {
        const row = { resource, severity: record.severityNumber ?? 0, attributes: attributes(record.attributes), record };

        (resource["service.name"] ? requests : events).push(row);
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

for (const name of [
  "system.cpu.time",
  "system.memory.usage",
  "system.filesystem.usage",
  "system.disk.io",
  "system.network.io",
  "system.paging.usage",
]) {
  expect(metrics.has(name), `${name} never arrived`);
}

expect(hostResources.length > 0, "no metrics arrived");
expect(
  hostResources.every((resource) => resource["host.name"] && resource["deployment.environment.name"] === "production"),
  "a metric without host.name or the environment",
);

// The Event Log: errors and worse only, each under the host.
expect(events.length > 0, "no Event Log error arrived");
expect(
  events.every((event) => event.severity >= 17),
  `an Event Log record milder than an error arrived: severity ${Math.min(...events.map((event) => event.severity))}`,
);
expect(
  events.every((event) => event.resource["host.name"]),
  "an Event Log record without host.name",
);
expect(
  events.every((event) => typeof event.record.body?.stringValue === "string" && event.attributes["windows.event.id"]),
  "an Event Log record whose text is not its message, or without its event ID",
);

if (ownEvent) {
  expect(
    events.some((event) => JSON.stringify(event.record.body ?? "").includes("Veyra's Windows check wrote this error.")),
    "the check's own Application error did not arrive",
  );
}

// IIS: every request a log of its site's service, 5xx an error, with the method, path, status, and time taken.
expect(requests.length > 0, "no IIS request arrived");
expect(
  requests.every((request) => /^W3SVC\d+$/.test(request.resource["service.name"]) && request.resource["host.name"]),
  "an IIS request without its site's service or host",
);
expect(
  requests.every(
    (request) =>
      request.attributes["http.request.method"] &&
      request.attributes["url.path"] &&
      request.attributes["http.response.status_code"] &&
      request.attributes["iis.time_taken_ms"] !== undefined &&
      request.attributes.date === undefined,
  ),
  "an IIS request without Veyra's attribute names, or with the raw W3C fields",
);
expect(
  requests.every((request) => request.record.timeUnixNano && request.record.timeUnixNano !== "0"),
  "an IIS request without its time",
);

if (iis) {
  expect([...metrics].some((name) => name.startsWith("iis.")), "IIS's own metrics never arrived");
  expect(
    requests.some((request) => request.attributes["http.response.status_code"] === "404" && request.severity === 13),
    "the 404 did not arrive as a warning",
  );
} else {
  const byStatus = Object.fromEntries(
    requests.map((request) => [request.attributes["http.response.status_code"], request.severity]),
  );

  expect(byStatus["200"] === 9, `the 200 is not info: ${byStatus["200"]}`);
  expect(byStatus["404"] === 13, `the 404 is not a warning: ${byStatus["404"]}`);
  expect(byStatus["500"] === 17, `the 500 is not an error: ${byStatus["500"]}`);
}

if (failures.length > 0) {
  console.error("Windows check failed:");

  for (const failure of failures) {
    console.error(`- ${failure}`);
  }

  process.exit(1);
}

console.log(
  `Windows check passed: ${metrics.size} metrics, ${events.length} Event Log errors, ${requests.length} IIS requests.`,
);
