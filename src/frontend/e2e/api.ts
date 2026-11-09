import type { Page, Request } from "@playwright/test";

import type { IngestionKey, LogEntry, LogSummary } from "../src/api/types";

/** A signed-in administrator. */
const session = { id: "u1", email: "ana@example.com", displayName: "Ana Admin", role: "Admin" };

/** 150 records, newest first: a payment incident among ordinary traffic. Enough for two pages of 100. */
const records: LogEntry[] = Array.from({ length: 150 }, (_, index): LogEntry => {
  const failing = index % 3 === 0;
  const timestamp = new Date(Date.UTC(2026, 10, 9, 7, 34, 12) - index * 1_700).toISOString();

  return {
    id: 1_000 - index,
    timestamp,
    observedTimestamp: timestamp,
    severity: failing ? "Error" : "Information",
    severityText: null,
    body: failing
      ? `Payment declined by PSP: upstream timeout after 1800 ms (#${index})`
      : `Order ${88_000 + index} created for customer ${1_000 + index}`,
    serviceName: failing ? "payments-gateway" : "orders-api",
    environment: "production",
    traceId: failing ? "5b8efff798038103d269b633813fc60c" : null,
    spanId: failing ? "eee19b7ec3c1b174" : null,
    attributes: failing
      ? {
          "http.route": "/checkout/confirm",
          "exception.stacktrace": "System.TimeoutException: The PSP did not answer\n   at PspClient.AuthorizeAsync()",
        }
      : { "http.route": "/orders" },
    resourceAttributes: { "service.name": failing ? "payments-gateway" : "orders-api", "host.name": "web-02" },
  };
});

const summary: LogSummary = {
  bucketSeconds: 120,
  buckets: Array.from({ length: 30 }, (_, index) => ({
    start: new Date(Date.UTC(2026, 10, 9, 6, 34) + index * 120_000).toISOString(),
    total: 60 + index * 4,
    errors: index > 15 ? index * 2 : 0,
  })),
  services: [
    { name: "payments-gateway", count: 4_812 },
    { name: "orders-api", count: 1_650 },
  ],
};

const keys: IngestionKey[] = [
  {
    id: "k1",
    name: "web-01 collector",
    displayPrefix: "vyk_AbCdEfGh",
    environments: ["production"],
    createdAt: "2026-11-01T09:00:00Z",
    expiresAt: null,
    revokedAt: null,
    lastUsedAt: "2026-11-09T07:30:00Z",
  },
];

/**
 * Answers the REST API inside the browser, as a signed-in administrator would see it.
 *
 * @param page - The page to answer for.
 * @returns The log searches the page made, newest last, so tests can check the filters it sent.
 */
export async function answerApi(page: Page): Promise<Request[]> {
  const searches: Request[] = [];

  await page.route("**/api/v1/**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());

    switch (url.pathname) {
      case "/api/v1/session":
        return route.fulfill({ json: session });
      case "/api/v1/session/antiforgery":
        return route.fulfill({ json: { token: "e2e" } });
      case "/api/v1/logs/summary":
        return route.fulfill({ json: summary });
      case "/api/v1/logs": {
        searches.push(request);
        const start = url.searchParams.get("cursor") === "page-2" ? 100 : 0;
        const page = records.slice(start, start + 100);

        return route.fulfill({ json: { items: page, nextCursor: start === 0 ? "page-2" : null } });
      }
      case "/api/v1/ingestion-keys":
        return route.fulfill({ json: keys });
      default:
        return route.fulfill({ status: 404, json: { title: "Not found" } });
    }
  });

  return searches;
}
