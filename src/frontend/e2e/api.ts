import type { Page, Request } from "@playwright/test";

import { historyItems, historySummary } from "../src/alerts/testAlertHistory";
import { openDiskFilling, openWarning, resolvedLatency } from "../src/alerts/testAlerts";
import type { AlertDetails, HomeSnapshot, IngestionKey, LogEntry, LogSummary } from "../src/api/types";
import { stormSnapshot } from "../src/home/testSnapshots";
import { durationChart, hostMetrics, names, overview, serviceMetrics } from "../src/metrics/testMetrics";
import { channels, emailServer } from "../src/notifications/testChannels";
import { lenaAccount, people, tomDetail } from "../src/users/testUsers";

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
 * @param options - What to answer that differs from the defaults.
 * @param options.home - The home snapshot; the storm from the VEY-17 mockups when left out.
 * @param options.alert - The alert every `/api/v1/alerts/{id}` answers with; the open warning of VEY-18 when left out.
 * @param options.signIn - `code` to start signed out, with a password step that asks for a code (VEY-20).
 * @returns The log searches the page made, newest last, so tests can check the filters it sent.
 */
export async function answerApi(
  page: Page,
  options: { home?: HomeSnapshot; alert?: AlertDetails; signIn?: "code" } = {},
): Promise<Request[]> {
  const searches: Request[] = [];
  let signedIn = options.signIn === undefined;

  await page.route("**/api/v1/**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());

    switch (url.pathname) {
      case "/api/v1/session":
        if (request.method() === "POST") {
          return route.fulfill({ status: 202, json: { next: "code" } });
        }

        return signedIn ? route.fulfill({ json: session }) : route.fulfill({ status: 401, json: { title: "Sign in" } });
      case "/api/v1/setup":
        return route.fulfill({ json: { required: false } });
      case "/api/v1/session/second-factor":
        signedIn = true;

        return route.fulfill({ json: session });
      case "/api/v1/users":
        return route.fulfill({ json: people });
      case "/api/v1/users/second-factor-policy":
        return route.fulfill({ json: { requiredFor: "Admins" } });
      case "/api/v1/account":
        return route.fulfill({ json: lenaAccount });
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
      case "/api/v1/home":
        return route.fulfill({ json: options.home ?? stormSnapshot });
      case "/api/v1/metrics/overview":
        return route.fulfill({ json: overview });
      case "/api/v1/metrics/names":
        return route.fulfill({ json: names });
      case "/api/v1/metrics/chart":
        return route.fulfill({ json: durationChart });
      case "/api/v1/notification-channels":
        return route.fulfill({ json: { emailServer, channels } });
      case "/api/v1/alerts":
        return route.fulfill({ json: { items: historyItems, next: null } });
      case "/api/v1/alerts/summary":
        return route.fulfill({ json: historySummary });
      case `/api/v1/alerts/${openDiskFilling.id}`:
        return route.fulfill({ json: openDiskFilling });
      case `/api/v1/alerts/${resolvedLatency.id}`:
        return route.fulfill({ json: resolvedLatency });
      default:
        if (url.pathname.startsWith("/api/v1/metrics/hosts/")) {
          return route.fulfill({ json: hostMetrics });
        }

        if (url.pathname.startsWith("/api/v1/metrics/services/")) {
          return route.fulfill({ json: serviceMetrics });
        }

        if (url.pathname.startsWith("/api/v1/users/")) {
          return route.fulfill({ json: tomDetail });
        }

        if (url.pathname.startsWith("/api/v1/alerts/")) {
          return route.fulfill({ json: options.alert ?? openWarning });
        }

        if (url.pathname.endsWith("/test")) {
          return route.fulfill({ json: { succeeded: true, status: 204, error: null } });
        }

        return route.fulfill({ status: 404, json: { title: "Not found" } });
    }
  });

  return searches;
}
