import type { Page, Request } from "@playwright/test";

import { historyItems, historySummary } from "../src/alerts/testAlertHistory";
import {
  integrityRule,
  openDiskFilling,
  openWarning,
  openWarningContext,
  resolvedLatency,
} from "../src/alerts/testAlerts";
import type { LookId } from "../src/api/looks";
import type { AlertDetails, HomeSnapshot, IngestionKey, LogEntry, LogSummary, SavedView } from "../src/api/types";
import { environmentChoicesOf, stormSnapshot } from "../src/home/testSnapshots";
import { incidentContext, incidentDetails, openIncident, resolvedIncident } from "../src/incidents/testIncidents";
import {
  branchIntegration,
  integrationList,
  moduleCatalog,
  pendingProbe,
  prodDetails,
  reportIntegration,
  succeededProbe,
} from "../src/integrations/testIntegrations";
import { jobDetailsById, jobList } from "../src/jobs/testJobs";
import { serverBackups, serverOverview } from "../src/metrics/database/testServerOverview";
import {
  availabilityView,
  blockingView,
  configurationView,
  permissionsGranted,
  eventsView,
  indexesView,
  jobsSimpleView,
  jobsView,
  liveView,
  queriesSimpleView,
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
import { overview as overviewAnswer } from "../src/overview/testOverview";
import { databasesList, hostsList, servicesList } from "../src/resources/testResources";
import { builtInAgent, postgresServers, sqlServers } from "../src/settings/testMonitoring";
import { listingOf, settingsHistory, settingsPage } from "../src/settings/testSettings";
import { checkoutErrors, checkoutTrace, durations, pspLog, tracesPage } from "../src/traces/testTraces";
import { lenaAccount, tomDetail, userList } from "../src/users/testUsers";

/** The figures of the traces of a window (ADR 0029). */
const traceSummary = {
  traces: 18_402,
  previousTraces: 17_360,
  failed: 3,
  slow: 920,
  p50Ms: 62,
  p95Ms: 214,
  services: 9,
};

/** A signed-in administrator. */
const session = {
  id: "u1",
  email: "ana@example.com",
  displayName: "Ana Admin",
  role: "Admin",
  timeZone: null,
  look: { chosen: null, default: "console", mayChoose: true },
};

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
          "exception.type": "System.TimeoutException",
          "exception.message": "The PSP did not answer",
          "exception.stacktrace": "System.TimeoutException: The PSP did not answer\n   at PspClient.AuthorizeAsync()",
        }
      : { "http.route": "/orders" },
    resourceAttributes: { "service.name": failing ? "payments-gateway" : "orders-api", "host.name": "web-02" },
    hostName: "web-02",
  };
});

const summary: LogSummary = {
  bucketSeconds: 120,
  buckets: Array.from({ length: 30 }, (_, index) => ({
    start: new Date(Date.UTC(2026, 10, 9, 6, 34) + index * 120_000).toISOString(),
    total: 60 + index * 4,
    errors: index > 15 ? index * 2 : 0,
    // Per level, as the server counts them (ADR 0024).
    levels: {
      Debug: 4,
      Information: 60 + index * 4 - 4 - 6 - (index > 15 ? index * 2 : 0),
      Warning: 6,
      Error: index > 15 ? index * 2 : 0,
    },
  })),
  services: [
    { name: "payments-gateway", count: 4_812 },
    { name: "orders-api", count: 1_650 },
  ],
  // The server adds up the window (VEY-68).
  total: 3_540,
  errors: 630,
  levels: { Debug: 120, Information: 2_610, Warning: 180, Error: 628, Fatal: 2 },
};

/** The saved views the server keeps (ADR 0024): one of the person's own and one someone shared. */
const savedViews: SavedView[] = [
  {
    id: "v1",
    name: "Checkout timeouts",
    screen: "logs",
    query: 'service:payments-gateway AND "timeout"',
    shared: false,
    mine: true,
    ownerName: null,
  },
  {
    id: "v2",
    name: "Orders",
    screen: "logs",
    query: "service:orders-api",
    shared: true,
    mine: false,
    ownerName: "Ana M.",
  },
];

/** The fields the query language knows (ADR 0024); any other name is an attribute. */
const queryFields = new Set(["service", "env", "environment", "host", "level", "trace", "span"]);

/** The levels the query language knows. */
const queryLevels = new Set(["trace", "debug", "info", "warning", "error", "fatal"]);

/**
 * Parses a log query as the server does, for the chips (`GET /api/v1/logs/parse`): a small stand-in that knows
 * the terms the tests write, with the place of each, and refuses an unknown level with its position.
 */
function parseQuery(query: string): { status: number; json: unknown } {
  const terms: unknown[] = [];
  const pattern = /([A-Za-z_][\w.]*)(:|>=)("[^"]*"|\S+)|"([^"]*)"|(\S+)/g;

  for (const match of query.matchAll(pattern)) {
    const start = match.index;
    const end = start + match[0].length;

    if (match[1] !== undefined && match[2] !== undefined && match[3] !== undefined) {
      const name = match[1];
      const value = match[3].replace(/^"|"$/g, "");

      if (name === "level" && !queryLevels.has(value.toLowerCase())) {
        return {
          status: 400,
          json: {
            title: "The query could not be read",
            detail: `Expected a level such as debug, info, warning, error, or fatal, not "${value}".`,
            position: start + name.length + match[2].length,
          },
        };
      }

      const field = queryFields.has(name) ? (name === "env" ? "environment" : name) : "attribute";
      terms.push({ field, op: match[2], value, ...(field === "attribute" ? { key: name } : {}), start, end });
    } else if (match[4] !== undefined) {
      terms.push({ field: "text", op: null, value: match[4], start, end });
    } else if (match[5] !== undefined && match[5] !== "AND") {
      terms.push({ field: "text", op: null, value: match[5], start, end });
    }
  }

  return { status: 200, json: { terms } };
}

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
 * server's own metrics for nothing, otherwise the tab's view (ADR 0020). With `simple=true` the server keeps to what
 * the Simple view shows (VEY-67).
 */
function databaseAnswer(url: URL): unknown {
  const [, tab = "", statement = ""] = databasePath.exec(url.pathname) ?? [];
  const simple = url.searchParams.get("simple") === "true";

  switch (tab) {
    case "live":
      return liveView;
    case "queries":
      return statement !== "" ? queryView : simple ? queriesSimpleView : queriesView;
    case "blocking":
      return blockingView;
    case "storage":
      return storageView;
    case "indexes":
      return indexesView;
    case "server":
      return serverView;
    case "jobs":
      return simple ? jobsSimpleView : jobsView;
    case "events":
      return eventsView;
    case "availability":
      return availabilityView;
    case "configuration":
      return configurationView;
    case "permissions":
      return permissionsGranted;
    case "overview":
      return serverOverview;
    case "backups":
      return serverBackups;
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
 * @param options.look - The look the person picked (VEY-71); none when left out, so they see the default, Console
 *   (ADR 0029). Forecast specs ask for Forecast.
 *   Picking one on the Account page is kept for the rest of the test, as the server would.
 * @returns The log searches the page made, newest last, so tests can check the filters it sent.
 */
export async function answerApi(
  page: Page,
  options: {
    home?: HomeSnapshot;
    alert?: AlertDetails;
    signIn?: "code";
    view?: "simple" | "advanced";
    look?: LookId;
    /** The builds the server answers, one a read and then the last for good, for the notice of a newer one (VEY-82). */
    builds?: { version: string; build: string }[];
  } = {},
): Promise<Request[]> {
  const searches: Request[] = [];
  const builds = [...(options.builds ?? [{ version: "0.4.1", build: "aaaaaaaaaaaa" }])];
  // The open incident, which acknowledging changes for the rest of the test, as the server would.
  const incident = { value: openIncident };
  const look = { chosen: (options.look ?? null) as string | null, default: "console", mayChoose: true };
  // The integrations as the test changes them (ADR 0028): each probe answers pending on its first read and succeeds on
  // the next, a server the wizard saves receives once the wizard asks again, and Update or Roll back sets a version.
  const probeReads = new Map<string, number>();
  const integrations = { added: null as { id: string; name: string } | null, version: "0.2.0" };

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

        return signedIn
          ? route.fulfill({ json: { ...session, look } })
          : route.fulfill({ status: 401, json: { title: "Sign in" } });
      case "/api/v1/setup":
        return route.fulfill({ json: { required: false } });
      case "/api/v1/session/second-factor":
        signedIn = true;

        return route.fulfill({ json: { ...session, look } });
      case "/api/v1/users":
        return route.fulfill({ json: userList });
      case "/api/v1/users/second-factor-policy":
        return route.fulfill({ json: { requiredFor: "Admins" } });
      case "/api/v1/account":
        return route.fulfill({ json: lenaAccount });
      case "/api/v1/account/time-zone":
        return route.fulfill({ status: 204 });
      case "/api/v1/search":
        // As the server answers (ADR 0022): each group ranked, the logs counted.
        return route.fulfill({
          json: {
            query: url.searchParams.get("q") ?? "",
            services: {
              items: [{ kind: "service", name: "payments-gateway", health: "Warning", detail: "4.1% errors" }],
              partial: false,
            },
            hosts: { items: [{ kind: "host", name: "web-01", health: "Ok", detail: "CPU 18%" }], partial: false },
            databases: { items: [], partial: false },
            traces: {
              items: [
                {
                  kind: "operation",
                  name: "POST /checkout",
                  service: "payments-gateway",
                  count: 18,
                  detail: "18 in 24 h",
                },
              ],
              partial: false,
            },
            logs: { count: 4812, partial: false },
          },
        });
      case "/api/v1/overview":
        return route.fulfill({ json: overviewAnswer });
      case "/api/v1/resources/services":
        return route.fulfill({ json: servicesList });
      case "/api/v1/resources/hosts":
        return route.fulfill({ json: hostsList });
      case "/api/v1/resources/databases":
        return route.fulfill({ json: databasesList });
      case "/api/v1/live":
        // One rate event; the browser's event source asks again when the answer ends.
        return route.fulfill({
          status: 200,
          headers: { "content-type": "text/event-stream" },
          body: ["event: rate", 'data: {"logs":180.2,"spans":42.5,"points":25.3}', "", ""].join("\n"),
        });
      case "/api/v1/account/look":
        look.chosen = (request.postDataJSON() as { look: string | null }).look;

        return route.fulfill({ status: 204 });
      case "/api/v1/session/antiforgery":
        return route.fulfill({ json: { token: "e2e" } });
      case "/api/v1/logs/summary":
        return route.fulfill({ json: summary });
      case "/api/v1/logs/parse":
        return route.fulfill(parseQuery(url.searchParams.get("q") ?? ""));
      case "/api/v1/views":
        if (request.method() === "POST") {
          const input = request.postDataJSON() as Omit<SavedView, "id" | "mine" | "ownerName">;
          const saved = { ...input, id: `v${savedViews.length + 1}`, mine: true, ownerName: null };
          savedViews.push(saved);

          return route.fulfill({ status: 201, json: saved });
        }

        return route.fulfill({ json: { items: savedViews } });
      case "/api/v1/logs": {
        searches.push(request);

        if (url.searchParams.get("traceId") === checkoutTrace.traceId) {
          // The server keeps the chosen span's records when asked by spanId (VEY-67).
          const spanId = url.searchParams.get("spanId");
          const items = spanId === null || spanId === pspLog.spanId ? [pspLog] : [];

          return route.fulfill({ json: { items, nextCursor: null } });
        }

        const start = url.searchParams.get("cursor") === "page-2" ? 100 : 0;
        const page = records.slice(start, start + 100);

        return route.fulfill({ json: { items: page, nextCursor: start === 0 ? "page-2" : null } });
      }
      case "/api/v1/ingestion-keys":
        return route.fulfill({ json: keys });
      case "/api/v1/home":
        return route.fulfill({ json: options.home ?? stormSnapshot });
      case "/api/v1/home/environments":
        return route.fulfill({
          json: environmentChoicesOf(
            (options.home ?? stormSnapshot).environments,
            url.searchParams.get("search") ?? "",
          ),
        });
      case "/api/v1/traces":
        return route.fulfill({ json: tracesPage });
      case "/api/v1/traces/durations":
        return route.fulfill({ json: durations });
      case "/api/v1/traces/services":
        // Most traces first, or with order=slowest the slowest p95 first (ADR 0029).
        return route.fulfill({
          json:
            url.searchParams.get("order") === "slowest"
              ? [
                  { service: "checkout-web", traces: 1, p95Ms: 1840, failed: 1 },
                  { service: "orders-api", traces: 2, p95Ms: 900, failed: 0 },
                ]
              : [
                  { service: "orders-api", traces: 2, p95Ms: 900, failed: 0 },
                  { service: "checkout-web", traces: 1, p95Ms: 1840, failed: 1 },
                ],
        });
      case "/api/v1/traces/summary":
        // The server counts the window under the filters (ADR 0029).
        return route.fulfill({ json: traceSummary });
      case `/api/v1/traces/${checkoutTrace.traceId}`:
        return route.fulfill({ json: url.searchParams.get("errorsOnly") === "true" ? checkoutErrors : checkoutTrace });
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
      case "/api/v1/settings/listing":
        return route.fulfill({
          json: listingOf(settingsPage, {
            search: url.searchParams.get("search") ?? "",
            section: url.searchParams.get("section") ?? "Data",
            view: url.searchParams.get("view") === "advanced" ? "advanced" : "simple",
          }),
        });
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
      case "/api/v1/about":
        return route.fulfill({ json: builds.length > 1 ? builds.shift() : builds[0] });
      case "/api/v1/about/releases":
        return route.fulfill({
          json: [
            {
              version: "0.4.2",
              date: "2026-12-01",
              summary: null,
              sections: [{ title: "Fixed", items: ["A fix that came with 0.4.2."] }],
            },
            {
              version: "0.4.1",
              date: "2026-11-27",
              summary: "Drill-in tiles and release notes.",
              sections: [{ title: "Added", items: ["Tiles open their details."] }],
            },
          ],
        });
      case "/api/v1/agents/summary":
        // The server counts the servers and agents for the Settings list (VEY-68).
        return route.fulfill({
          json: {
            sqlServers: sqlServers.length,
            postgresServers: postgresServers.length,
            needLook: 1,
            agents: 1,
            agentsOnline: 1,
          },
        });
      case "/api/v1/integrations": {
        const added = integrations.added;
        const items = [...integrationList.items];

        // A server the wizard saved has its first figures by the time the wizard asks again.
        if (added) {
          items.push({ ...branchIntegration, id: added.id, name: added.name, state: "Receiving" });
        }

        return route.fulfill({ json: { items } });
      }
      case "/api/v1/sql-servers": {
        if (request.method() === "POST") {
          const input = request.postDataJSON() as { name: string; address: string };
          integrations.added = { id: "s4", name: input.name };

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
        return route.fulfill({ json: [integrityRule] });
      case "/api/v1/alerts":
        return route.fulfill({ json: { items: historyItems, next: null } });
      case "/api/v1/alerts/summary":
        return route.fulfill({ json: historySummary });
      case "/api/v1/alerts/areas":
        // The server ranks the areas (VEY-68); without alerts each is fine, in the order asked.
        return route.fulfill({
          json: url.searchParams.getAll("area").map((area) => ({ area, health: "Ok", alerts: [] })),
        });
      case `/api/v1/alerts/${openDiskFilling.id}`:
        return route.fulfill({ json: openDiskFilling });
      case `/api/v1/alerts/${resolvedLatency.id}`:
        return route.fulfill({ json: resolvedLatency });
      default:
        if (url.pathname.startsWith("/api/v1/metrics/hosts/")) {
          return route.fulfill({ json: hostMetrics });
        }

        if (url.pathname.startsWith("/api/v1/metrics/databases/")) {
          return route.fulfill({ json: databaseAnswer(url) });
        }

        if (url.pathname.startsWith("/api/v1/metrics/services/")) {
          return route.fulfill({ json: serviceMetrics });
        }

        if (url.pathname.startsWith("/api/v1/users/")) {
          return route.fulfill({ json: tomDetail });
        }

        if (url.pathname === "/api/v1/jobs") {
          // The server filters by status, source, and the search, failing jobs first (ADR 0027).
          const status = url.searchParams.get("status") ?? "all";
          const source = url.searchParams.get("source");
          const q = (url.searchParams.get("q") ?? "").toLowerCase();
          const items = jobList.items.filter(
            (job) =>
              (status === "all" ||
                (status === "failing" && job.lastRun?.status === "failed") ||
                (status === "running" && job.running)) &&
              (source === null || job.source === source) &&
              (q === "" || `${job.name} ${job.block.name}`.toLowerCase().includes(q)),
          );

          return route.fulfill({
            json: { ...jobList, figures: { ...jobList.figures, jobs: items.length }, items },
          });
        }

        if (url.pathname.startsWith("/api/v1/jobs/")) {
          const job = jobDetailsById[decodeURIComponent(url.pathname.slice("/api/v1/jobs/".length))];

          return job ? route.fulfill({ json: job }) : route.fulfill({ status: 404, json: { title: "Not found" } });
        }

        if (url.pathname === "/api/v1/incidents") {
          // The server filters by state and orders open ones first (ADR 0026).
          const state = url.searchParams.get("state") ?? "all";
          const items = [incident.value, resolvedIncident].filter((one) => state === "all" || one.state === state);

          return route.fulfill({ json: { items, next: null } });
        }

        if (url.pathname.endsWith("/acknowledge")) {
          const acknowledged = request.method() === "POST";
          incident.value = {
            ...incident.value,
            state: acknowledged ? "acknowledged" : "open",
            acknowledgedAt: acknowledged ? new Date().toISOString() : null,
            acknowledgedBy: acknowledged ? "Lena Admin" : null,
          };

          return route.fulfill({ json: incident.value });
        }

        if (url.pathname.startsWith("/api/v1/alerts/") && url.pathname.endsWith("/context")) {
          return route.fulfill({ json: openWarningContext });
        }

        if (url.pathname.endsWith("/context")) {
          return route.fulfill({ json: incidentContext });
        }

        if (url.pathname.startsWith("/api/v1/incidents/")) {
          return route.fulfill({ json: { ...incidentDetails, incident: incident.value } });
        }

        if (url.pathname.startsWith("/api/v1/alerts/")) {
          return route.fulfill({ json: options.alert ?? openWarning });
        }

        if (url.pathname === "/api/v1/integrations/modules/sqlserver") {
          return route.fulfill({ json: moduleCatalog });
        }

        if (url.pathname === "/api/v1/integrations/probes" && request.method() === "POST") {
          const id = `probe-${probeReads.size + 1}`;
          probeReads.set(id, 0);

          return route.fulfill({ status: 202, json: { id } });
        }

        if (url.pathname.startsWith("/api/v1/integrations/probes/")) {
          const id = url.pathname.slice("/api/v1/integrations/probes/".length);
          const reads = (probeReads.get(id) ?? 0) + 1;
          probeReads.set(id, reads);

          return route.fulfill({ json: { ...(reads === 1 ? pendingProbe : succeededProbe), id } });
        }

        if (url.pathname.startsWith("/api/v1/integrations/sqlserver/")) {
          const [id = "", action] = url.pathname.slice("/api/v1/integrations/sqlserver/".length).split("/");

          if (action === "version" && request.method() === "POST") {
            integrations.version = (request.postDataJSON() as { version: string }).version;

            return route.fulfill({ json: { ...prodDetails.integration, version: integrations.version } });
          }

          if (id === prodDetails.integration.id) {
            const version = integrations.version;

            return route.fulfill({
              json: {
                ...prodDetails,
                integration: { ...prodDetails.integration, version },
                package: { ...prodDetails.package, version },
              },
            });
          }

          if (id === reportIntegration.id) {
            return route.fulfill({
              json: {
                ...prodDetails,
                integration: reportIntegration,
                package: { ...prodDetails.package, version: "0.1.0", previousVersion: "0.1.0" },
              },
            });
          }

          return route.fulfill({ status: 404, json: { title: "Not found" } });
        }

        if (url.pathname.startsWith("/api/v1/sql-servers/") && request.method() === "DELETE") {
          return route.fulfill({ status: 204 });
        }

        if (url.pathname.startsWith("/api/v1/sql-servers/") && request.method() === "PUT") {
          const id = url.pathname.slice("/api/v1/sql-servers/".length);
          const input = request.postDataJSON() as Record<string, unknown>;
          const stored = sqlServers.find((server) => server.id === id) ?? sqlServers[0];

          return route.fulfill({ json: { ...stored, ...input, password: undefined } });
        }

        if (url.pathname.endsWith("/test")) {
          return route.fulfill({ json: { succeeded: true, status: 204, error: null } });
        }

        return route.fulfill({ status: 404, json: { title: "Not found" } });
    }
  });

  return searches;
}
