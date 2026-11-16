// Benchmark (ADR 0015): live traffic while measuring. Signs in to the benchmark installation as its admin, creates an
// ingestion key, then sends RATE log records a second over OTLP/HTTP, one batch a second, until stopped. The key's
// secret stays in this process. BASE, OTLP, RATE, SHOT_EMAIL, and SHOT_PASSWORD come from the environment.
const path = require("path");
const { chromium } = require(path.join(__dirname, "../../src/frontend/node_modules/@playwright/test"));

const base = process.env.BASE ?? "http://localhost:8091";
const otlp = process.env.OTLP ?? "http://localhost:24318";
const rate = Number(process.env.RATE ?? 35);
const services = ["checkout-web", "orders-api", "payments-gateway", "search", "catalog-api", "identity",
  "notifications", "billing-worker", "reporting", "inventory", "shipping", "psp-client"];
const hex = (n) => [...Array(n)].map(() => "0123456789abcdef"[Math.floor(Math.random() * 16)]).join("");

async function createKey() {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  await page.goto(base);
  await page.getByLabel("Email").fill(process.env.SHOT_EMAIL);
  await page.getByLabel("Password").fill(process.env.SHOT_PASSWORD);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.getByLabel("Password").waitFor({ state: "detached" });
  const { token } = await (await page.request.get(`${base}/api/v1/session/antiforgery`)).json();
  const response = await page.request.post(`${base}/api/v1/ingestion-keys`, {
    headers: { "X-Veyra-Antiforgery": token },
    data: { name: `bench load ${new Date().toISOString()}`, environments: null, expiresAt: null },
  });
  const { secret } = await response.json();
  await browser.close();

  return secret;
}

function batch() {
  const byService = new Map();
  const now = Date.now();

  for (let i = 0; i < rate; i++) {
    const service = services[Math.floor(services.length * Math.random() ** 1.6)];
    const roll = Math.random();
    const severity = roll < 0.15 ? 5 : roll < 0.85 ? 9 : roll < 0.95 ? 13 : 17;
    const body =
      severity === 17
        ? "Database query failed: deadlock detected on relation orders"
        : severity === 13
          ? `Slow response from PSP /authorize: ${800 + Math.floor(Math.random() * 2000)} ms`
          : `GET /orders/${Math.floor(Math.random() * 900000)} 200 ${3 + Math.floor(Math.random() * 80)} ms`;
    const record = {
      timeUnixNano: String((now - Math.floor(Math.random() * 1000)) * 1_000_000),
      severityNumber: severity,
      body: { stringValue: body },
      attributes: [{ key: "http.route", value: { stringValue: "/orders/{id}" } }],
      traceId: Math.random() < 0.4 ? hex(32) : undefined,
    };
    (byService.get(service) ?? byService.set(service, []).get(service)).push(record);
  }

  return [...byService].map(([service, logRecords]) => ({
    resource: {
      attributes: [
        { key: "service.name", value: { stringValue: service } },
        { key: "deployment.environment", value: { stringValue: Math.random() < 0.7 ? "production" : "staging" } },
      ],
    },
    scopeLogs: [{ logRecords }],
  }));
}

(async () => {
  const key = await createKey();
  console.log(`sending ${rate} records a second`);

  setInterval(async () => {
    try {
      const response = await fetch(`${otlp}/v1/logs`, {
        method: "POST",
        headers: { "Content-Type": "application/json", Authorization: `Bearer ${key}` },
        body: JSON.stringify({ resourceLogs: batch() }),
      });

      if (!response.ok) {
        console.log("ingestion answered", response.status);
      }
    } catch (error) {
      console.log("send failed", String(error));
    }
  }, 1000);
})();
