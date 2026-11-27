// Benchmark (ADR 0015, ADR 0014 for traces): asks for every read the screens make, RUNS times each after one warm-up,
// signed in, and checks the 95th percentile against its budget. Prints a Markdown table and exits with 1 when any
// request is over budget.
// Times are as the client sees them, which on the same machine is close to the server's.
//
// BASE, SHOT_EMAIL, and SHOT_PASSWORD come from the environment; RUNS defaults to 20; ONLY is a regular expression
// that picks requests by name.
const path = require("path");
const { chromium } = require(path.join(__dirname, "../../src/frontend/node_modules/@playwright/test"));

const base = process.env.BASE ?? "http://localhost:8091";
const runs = Number(process.env.RUNS ?? 20);
const now = new Date();
const ago = (ms) => new Date(now.getTime() - ms).toISOString();
const to = now.toISOString();
const hour = 3_600_000;
const day = 24 * hour;

const window = (span) => `from=${encodeURIComponent(ago(span))}&to=${encodeURIComponent(to)}`;
const route = encodeURIComponent("http.route:/pay");
const searchRoute = encodeURIComponent("http.route:/search");

// Name, path, and budget in milliseconds from ADR 0015; null is measured but has no budget.
const requests = [
  ["Home", "/api/v1/home?environment=production", 150],
  ["Home, all environments", "/api/v1/home", 150],
  ["Logs: last hour", `/api/v1/logs?${window(hour)}&limit=100`, 100],
  ["Logs: 7 days", `/api/v1/logs?${window(7 * day)}&limit=100`, 100],
  ["Logs: 7 days, a word", `/api/v1/logs?${window(7 * day)}&text=deadlock&limit=100`, 100],
  // load.js never writes "order", so the newest match is where the seed ended: a common word that stopped (ADR 0016).
  ["Logs: 7 days, a common word that stopped", `/api/v1/logs?${window(7 * day)}&text=order&limit=100`, 100],
  ["Logs: 7 days, errors", `/api/v1/logs?${window(7 * day)}&severity=Error&limit=100`, 100],
  ["Logs: 7 days, fatal", `/api/v1/logs?${window(7 * day)}&severity=Fatal&limit=100`, 100],
  ["Logs: 7 days, a quiet service", `/api/v1/logs?${window(7 * day)}&service=psp-client&limit=100`, 100],
  ["Logs: 24 h, an attribute", `/api/v1/logs?${window(day)}&attribute=${route}&limit=100`, 100],
  ["Logs: a trace", `/api/v1/logs?${window(7 * day)}&traceId=00000000000000000000000000000000&limit=100`, 100],
  ["Log counts: last hour", `/api/v1/logs/summary?${window(hour)}`, 100],
  ["Log counts: 24 h", `/api/v1/logs/summary?${window(day)}`, 100],
  ["Log counts: 7 days", `/api/v1/logs/summary?${window(7 * day)}`, 100],
  ["Log counts: 14 days", `/api/v1/logs/summary?${window(14 * day)}`, 100],
  ["Log counts: 7 days, errors", `/api/v1/logs/summary?${window(7 * day)}&severity=Error`, 100],
  ["Log counts: 7 days, a service", `/api/v1/logs/summary?${window(7 * day)}&service=checkout-web`, 100],
  ["Log counts: 24 h, a word", `/api/v1/logs/summary?${window(day)}&text=deadlock`, 300],
  ["Log counts: 24 h, an attribute", `/api/v1/logs/summary?${window(day)}&attribute=${route}`, 300],
  ["Log counts: 7 days, a word", `/api/v1/logs/summary?${window(7 * day)}&text=deadlock`, null],
  ["Metrics overview: 1 h", "/api/v1/metrics/overview?range=1h", 200],
  ["Metrics overview: 6 h", "/api/v1/metrics/overview?range=6h", 200],
  ["Metrics overview: 24 h", "/api/v1/metrics/overview?range=24h", 200],
  ["Metrics overview: 7 days", "/api/v1/metrics/overview?range=7d", 200],
  ["Host: 6 h", "/api/v1/metrics/hosts/web-01?range=6h", 200],
  ["Host: 7 days", "/api/v1/metrics/hosts/web-01?range=7d", 200],
  ["Service: 6 h", "/api/v1/metrics/services/checkout-web?environment=production&range=6h", 200],
  ["Service: 7 days", "/api/v1/metrics/services/checkout-web?environment=production&range=7d", 200],
  ["Metric names: 7 days", "/api/v1/metrics/names?range=7d", 200],
  ["Metric chart: 24 h", "/api/v1/metrics/chart?name=system.cpu.time&range=24h", 200],
  ["Metric chart: 7 days", "/api/v1/metrics/chart?name=system.cpu.time&range=7d", 200],
  // ADR 0022: any from-to window, and the Resources lists with their comparison to the period before.
  ["Metrics overview: 30 days, from-to", `/api/v1/metrics/overview?${window(30 * day)}`, 200],
  ["Metric chart: 30 days, from-to", `/api/v1/metrics/chart?name=system.cpu.time&${window(30 * day)}`, 200],
  ["Resources: services, 1 h", "/api/v1/resources/services?range=1h", 200],
  ["Resources: services, 7 days", "/api/v1/resources/services?range=7d", 200],
  ["Resources: hosts, 24 h", "/api/v1/resources/hosts?range=24h", 200],
  ["Resources: hosts, 7 days", "/api/v1/resources/hosts?range=7d", 200],
  ["Resources: databases, 24 h", "/api/v1/resources/databases?range=24h", 200],
  // ADR 0027: every job added up in the database, and one job; {job} is the first job of the list, after signing in.
  ["Jobs: 24 h", "/api/v1/jobs?range=24h", 200],
  ["Jobs: 30 days", "/api/v1/jobs?range=30d", 200],
  ["Jobs: 30 days, failing", "/api/v1/jobs?range=30d&status=failing", 200],
  ["Job: 30 days", "/api/v1/jobs/{job}?range=30d", 200],
  ["Alerts: 30 days", "/api/v1/alerts?days=30&timeZone=Europe%2FBerlin", 100],
  ["Alert summary: 90 days", "/api/v1/alerts/summary?days=90&timeZone=Europe%2FBerlin", 100],
  // {trace} and {at} are a recent checkout trace and its start, looked up after signing in.
  ["Traces: one trace, known time", "/api/v1/traces/{trace}?at={at}", 50],
  ["Traces: one trace, bare ID", "/api/v1/traces/{trace}", null],
  ["Traces: logs of a trace", "/api/v1/logs?traceId={trace}&from={from}&to={to}", 50],
  ["Traces: a service, last hour", "/api/v1/traces?service=checkout-web", 100],
  ["Traces: all services, last hour", "/api/v1/traces", 100],
  [
    "Traces: failed and slow, 24 h",
    `/api/v1/traces?${window(day)}&service=checkout-web&failed=true&minDurationMs=300`,
    300,
  ],
  ["Traces: a name, 24 h", `/api/v1/traces?${window(day)}&name=orders`, 300],
  ["Traces: an attribute, 24 h", `/api/v1/traces?${window(day)}&attribute=${searchRoute}`, 300],
  ["Traces: slow, 24 h", `/api/v1/traces?${window(day)}&slow=true`, 300],
  ["Traces: duration chart, 1 h", "/api/v1/traces/durations", null],
  ["Traces: duration chart, 24 h", `/api/v1/traces/durations?${window(day)}`, null],
  ["Traces: services, 24 h", `/api/v1/traces/services?${window(day)}`, null],
  ["Account", "/api/v1/account", 50],
  ["Users", "/api/v1/users", 50],
  ["Session", "/api/v1/session", 50],
];

function percentile(values, p) {
  const sorted = [...values].sort((a, b) => a - b);

  return sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))];
}

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  await page.goto(base);
  await page.getByLabel("Email").fill(process.env.SHOT_EMAIL);
  await page.getByLabel("Password").fill(process.env.SHOT_PASSWORD);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.getByLabel("Password").waitFor({ state: "detached" });

  // A recent trace to open, as a click in the list would.
  const recent = await (await page.request.get(`${base}/api/v1/traces?service=checkout-web&limit=1`)).json();
  const trace = recent.items?.[0];
  const jobs = await (await page.request.get(`${base}/api/v1/jobs?range=30d`)).json();
  const job = jobs.items?.[0];
  const fill = (path) =>
    path
      .replace("{trace}", trace?.traceId ?? "00000000000000000000000000000000")
      .replace("{job}", job?.id ?? "none")
      .replace("{at}", encodeURIComponent(trace?.start ?? to))
      .replace("{from}", encodeURIComponent(ago(hour)))
      .replace("{to}", encodeURIComponent(to));

  const chosen = requests
    .filter(([name]) => !process.env.ONLY || new RegExp(process.env.ONLY).test(name))
    .map(([name, path, budget]) => [name, fill(path), budget]);
  const over = [];

  console.log("| Request | Status | p50 ms | p95 ms | Budget ms | Answer |");
  console.log("| --- | --- | --- | --- | --- | --- |");

  for (const [name, path, budget] of chosen) {
    const times = [];
    let status = 0;
    let size = 0;

    for (let i = 0; i <= runs; i++) {
      const started = performance.now();
      const response = await page.request.get(`${base}${path}`, { timeout: 120_000 });
      const body = await response.body();
      const took = performance.now() - started;

      // The first run warms the cache, as a screen that was just opened would.
      if (i > 0) {
        times.push(took);
      }

      status = response.status();
      size = body.length;
    }

    const p95 = percentile(times, 95);
    const failed = status !== 200 || (budget !== null && p95 > budget);
    const kb = size > 1024 ? `${(size / 1024).toFixed(0)} KB` : `${size} B`;
    const mark = failed ? " **over**" : "";

    if (failed) {
      over.push(name);
    }

    const p50 = percentile(times, 50).toFixed(0);
    console.log(`| ${name} | ${status} | ${p50} | ${p95.toFixed(0)}${mark} | ${budget ?? "none"} | ${kb} |`);
  }

  await browser.close();

  if (over.length > 0) {
    console.log(`\nOver budget or failed: ${over.join(", ")}`);
    process.exit(1);
  }

  console.log(`\nAll ${chosen.length} requests within budget.`);
})();
