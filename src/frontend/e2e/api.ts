import type { Page, Request } from "@playwright/test";

import { historyItems, historySummary } from "../src/alerts/testAlertHistory";
import { openDiskFilling, openWarning, resolvedLatency } from "../src/alerts/testAlerts";
import type { AlertDetails, HomeSnapshot, IngestionKey, LogEntry, LogSummary } from "../src/api/types";
import { stormSnapshot } from "../src/home/testSnapshots";
import {
  availabilityView,
  blockingView,
  configurationView,
  permissionsGranted,
  eventsView,
  indexesView,
  jobsView,
  liveView,
  queriesView,
  queryView,
  serverView,
  storageView,
} from "../src/metrics/database/testDatabase";
import {
  databaseMetrics,
  durationChart,
  hostMetrics,
  names,
  overview,
  serviceMetrics,
} from "../src/metrics/testMetrics";
import { channels, emailServer } from "../src/notifications/testChannels";
import { builtInAgent, postgresServers, sqlServers } from "../src/settings/testMonitoring";
import { settingsHistory, settingsPage } from "../src/settings/testSettings";
import { checkoutTrace, durations, pspLog, tracesPage } from "../src/traces/testTraces";
import { lenaAccount, people, tomDetail } from "../src/users/testUsers";

/** A signed-in administrator. */
const session = { id: "u1", email: "ana@example.com", displayName: "Ana Admin", role: "Admin", timeZone: null };

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

/** A path under `/api/v1/metrics/databases/`: the engine, the server, then the tab's name and what follows it. */
const databasePath = /^\/api\/v1\/metrics\/databases\/[^/]+\/[^/]+(?:\/([^/]+)(?:\/(.+))?)?$/;

/**
 * The answer for one path under `/api/v1/metrics/databases/`, by what follows the engine and the server: the
 * server's own metrics for nothing, otherwise the tab's view (ADR 0020).
 */
function databaseAnswer(pathname: string): unknown {
  const [, tab = "", statement = ""] = databasePath.exec(pathname) ?? [];

  switch (tab) {
    case "live":
      return liveView;
    case "queries":
      return statement === "" ? queriesView : queryView;
    case "blocking":
      return blockingView;
    case "storage":
      return storageView;
    case "indexes":
      return indexesView;
    case "server":
      return serverView;
    case "jobs":
      return jobsView;
    case "events":
      return eventsView;
    case "availability":
      return availabilityView;
    case "configuration":
      return configurationView;
    case "permissions":
      return permissionsGranted;
    default:
      return databaseMetrics;
  }
}

/**
 * Answers the REST API inside the browser, as a signed-in administrator would see it.
 *
 * @param page - The page to answer for.
 * @param options - What to answer that differs from the defaults.
 * @param options.home - The home snapshot; the storm from the VEY-17 mockups when left out.
 * @param options.alert - The alert every `/api/v1/alerts/{id}` answers with; the open warning of VEY-18 when left out.
 * @param options.signIn - `code` to start signed out, with a password step that asks for a code (VEY-20).
 * @param options.view - The Simple or Advanced view the browser starts in (VEY-37). Advanced when left out, so a test
 *   sees every part of a screen; a choice the page makes later is kept across reloads.
 * @returns The log searches the page made, newest last, so tests can check the filters it sent.
 */
export async function answerApi(
  page: Page,
  options: { home?: HomeSnapshot; alert?: AlertDetails; signIn?: "code"; view?: "simple" | "advanced" } = {},
): Promise<Request[]> {
  const searches: Request[] = [];

  // Only when the browser has no choice yet, so a test that switches the view and reloads keeps its own.
  await page.addInitScript((view) => {
    if (localStorage.getItem("veyra.view") === null) {
      localStorage.setItem("veyra.view", view);
    }
  }, options.view ?? "advanced");
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
      case "/api/v1/account/time-zone":
        return route.fulfill({ status: 204 });
      case "/api/v1/session/antiforgery":
        return route.fulfill({ json: { token: "e2e" } });
      case "/api/v1/logs/summary":
        return route.fulfill({ json: summary });
      case "/api/v1/logs": {
        searches.push(request);

        if (url.searchParams.get("traceId") === checkoutTrace.traceId) {
          return route.fulfill({ json: { items: [pspLog], nextCursor: null } });
        }

        const start = url.searchParams.get("cursor") === "page-2" ? 100 : 0;
        const page = records.slice(start, start + 100);

        return route.fulfill({ json: { items: page, nextCursor: start === 0 ? "page-2" : null } });
      }
      case "/api/v1/ingestion-keys":
        return route.fulfill({ json: keys });
      case "/api/v1/home":
        return route.fulfill({ json: options.home ?? stormSnapshot });
      case "/api/v1/traces":
        return route.fulfill({ json: tracesPage });
      case "/api/v1/traces/durations":
        return route.fulfill({ json: durations });
      case "/api/v1/traces/services":
        return route.fulfill({
          json: [
            { service: "checkout-web", traces: 1 },
            { service: "orders-api", traces: 1 },
          ],
        });
      case `/api/v1/traces/${checkoutTrace.traceId}`:
        return route.fulfill({ json: checkoutTrace });
      case "/api/v1/metrics/overview":
        return route.fulfill({ json: overview });
      case "/api/v1/metrics/names":
        return route.fulfill({ json: names });
      case "/api/v1/metrics/chart":
        return route.fulfill({ json: durationChart });
      case "/api/v1/notification-channels":
        return route.fulfill({ json: { emailServer, channels } });
      case "/api/v1/settings":
        return route.fulfill({ json: settingsPage });
      case "/api/v1/settings/review": {
        // Every change passes and says nothing, from the value in effect to the one sent.
        const { changes } = request.postDataJSON() as { changes: { key: string; value: string | null }[] };
        const reviewed = changes.map((change) => ({
          key: change.key,
          from: settingsPage.settings.find((setting) => setting.key === change.key)?.value ?? null,
          to: change.value,
        }));

        return route.fulfill({ json: { problems: [], notes: [], changes: reviewed, confirmationRequired: false } });
      }
      case "/api/v1/settings/history":
        return route.fulfill({ json: settingsHistory });
      case "/api/v1/agents": {
        if (request.method() === "POST") {
          const input = request.postDataJSON() as { name: string };
          const agent = { ...builtInAgent, id: "a2", name: input.name, placement: "Remote", state: "NotConnected" };

          return route.fulfill({
            status: 201,
            json: { agent: { ...agent, installed: false }, code: "K7QM-4TWX-9PRA", expiresAt: "2026-11-13T19:23:00Z" },
          });
        }

        return route.fulfill({ json: [builtInAgent] });
      }
      case "/api/v1/sql-servers": {
        if (request.method() === "POST") {
          const input = request.postDataJSON() as { name: string; address: string };

          return route.fulfill({
            status: 201,
            json: { ...sqlServers[2], id: "s4", name: input.name, address: input.address },
          });
        }

        return route.fulfill({ json: sqlServers });
      }
      case "/api/v1/postgres-servers": {
        if (request.method() === "POST") {
          const input = request.postDataJSON() as { name: string; address: string };

          return route.fulfill({
            status: 201,
            json: {
              ...postgresServers[0],
              id: "p2",
              name: input.name,
              address: input.address,
              state: "WaitingForData",
              since: null,
            },
          });
        }

        return route.fulfill({ json: postgresServers });
      }
      case "/api/v1/alerts/catalog":
        return route.fulfill({ json: [] });
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

        if (url.pathname.startsWith("/api/v1/metrics/databases/")) {
          return route.fulfill({ json: databaseAnswer(url.pathname) });
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
