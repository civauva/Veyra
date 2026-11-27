import type {
  About,
  Release,
  SettingChangeInput,
  SettingsFile,
  SettingsHistory,
  SettingsListing,
  SettingsListingChoices,
  SettingsPage,
  SettingsReview,
  Account,
  EnvironmentChoices,
  AddedAgent,
  AgentsSummary,
  Agent,
  AgentCode,
  AgentHostInput,
  AlertContext,
  AlertDetails,
  AlertAreaState,
  AlertList,
  CatalogRule,
  DatabaseBlocking,
  DatabaseLive,
  DatabaseQueries,
  DatabaseQuery,
  LiveFilterValues,
  QuerySort,
  StorageView,
  IndexAdvice,
  ServerView,
  AgentJobsView,
  ServerEvent,
  AvailabilityView,
  ConfigurationView,
  PermissionsView,
  AlertQuery,
  AlertSummary,
  ChannelInput,
  DatabaseMetrics,
  CreatedIngestionKey,
  EmailServer,
  HostMetrics,
  HomeSnapshot,
  IngestionKey,
  LogsPage,
  LogQueryParse,
  LogSummary,
  SavedView,
  IncidentContext,
  IncidentDetails,
  IncidentItem,
  IncidentPage,
  IntegrationDetails,
  IntegrationItem,
  IntegrationList,
  IntegrationModuleInfo,
  Probe,
  ProbeInput,
  IncidentState,
  JobDetails,
  JobList,
  JobQuery,
  ServerBackups,
  ServerOverview,
  SavedViewInput,
  MetricChart,
  MetricNameInfo,
  MetricRange,
  MetricsOverview,
  MetricsOverviewChoices,
  NewUser,
  NotificationChannel,
  PasswordStep,
  PostgresServer,
  PostgresServerInput,
  SecondFactorPolicy,
  SecondFactorSetup,
  SendResult,
  SqlServer,
  SqlServerInput,
  ServiceMetrics,
  Session,
  SignInNext,
  UserChanges,
  UserDetail,
  UserList,
  UserListChoices,
  UserSummary,
  Trace,
  DatabaseServerRow,
  DatabasesHeader,
  HostRow,
  HostsHeader,
  Overview,
  ResourcePage,
  ResourceQuery,
  SearchResults,
  ServiceRow,
  ServicesHeader,
  TraceDurations,
  TraceService,
  TracesPage,
  TracesSummary,
} from "./types";
import type { LookId } from "./looks";
import { displayZone } from "../time/zone";

/**
 * A request the Veyra API refused, with the title of its problem details (RFC 9457) as the message.
 */
export class ApiError extends Error {
  /**
   * @param status - The HTTP status code.
   * @param message - A message that can be shown to the person.
   * @param body - The parsed JSON body of the answer, for answers that carry more than a title, such as a refused
   *   save of settings with its review.
   */
  constructor(
    public readonly status: number,
    message: string,
    public readonly body: unknown = undefined,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

/** The header that carries the antiforgery token on requests that change state (ADR 0008). */
const antiforgeryHeader = "X-Veyra-Antiforgery";

/**
 * The antiforgery token of the current session. It lives only in memory: after a reload it is fetched again, and a
 * script injected into the page cannot read it from storage.
 */
let antiforgeryToken: string | null = null;

/** Forgets the token, so tests start from a clean state. */
export function resetAntiforgeryForTests(): void {
  antiforgeryToken = null;
}

interface RequestOptions {
  /** The HTTP method; GET when left out. */
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  /** A value to send as JSON. */
  body?: unknown;
  /** Cancels the request, for example when the person changes a filter before the answer arrives. */
  signal?: AbortSignal;
}

/**
 * Calls the Veyra API on the same origin, with the session cookie and, for writes, the antiforgery token.
 *
 * @param path - The path, for example `/api/v1/logs?limit=100`.
 * @param options - Method, body, and cancellation.
 * @returns The parsed JSON, or `undefined` for answers without a body.
 * @throws {@link ApiError} when the API answers with an error status.
 */
async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const method = options.method ?? "GET";
  const headers = new Headers({ Accept: "application/json" });

  if (options.body !== undefined) {
    headers.set("Content-Type", "application/json");
  }

  if (method !== "GET" && antiforgeryToken) {
    headers.set(antiforgeryHeader, antiforgeryToken);
  }

  const response = await fetch(path, {
    method,
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    credentials: "same-origin",
    signal: options.signal,
  });

  if (!response.ok) {
    const { title, body } = await problemOf(response);

    throw new ApiError(response.status, title, body);
  }

  const text = await response.text();

  return (text ? JSON.parse(text) : undefined) as T;
}

/** Reads an error answer: the title of its problem details, with a fallback, and its body when it is JSON. */
async function problemOf(response: Response): Promise<{ title: string; body: unknown }> {
  const fallback = `Veyra answered with status ${response.status}.`;

  try {
    const body = (await response.json()) as { title?: unknown } | null;
    const title = typeof body?.title === "string" && body.title.length > 0 ? body.title : fallback;

    return { title, body };
  } catch {
    // Not JSON: only the generic message.
    return { title: fallback, body: undefined };
  }
}

/** Fetches a fresh antiforgery token. Needed after every sign-in, because the token is bound to the session. */
async function refreshAntiforgery(): Promise<void> {
  const { token } = await request<{ token: string }>("/api/v1/session/antiforgery");

  antiforgeryToken = token;
}

/**
 * The query parameters that name a metrics range (ADR 0022): `range` for a preset ending now, or `from` and `to`
 * for a window of the person's own.
 *
 * @param range - The range, a preset or `from~to`.
 * @returns `{ range }`, or `{ from, to }`.
 */
export function rangeParams(range: MetricRange): Record<string, string> {
  const split = range.indexOf("~");

  if (split < 0) {
    return { range };
  }

  return { from: range.slice(0, split), to: range.slice(split + 1) };
}

/**
 * The address of one of a database server's endpoints, with its query. Values left empty are left out, so the server
 * applies its defaults.
 *
 * @param engine - As `db.system.name` spells it.
 * @param server - The server.
 * @param path - The endpoint after the server, such as `/queries`, or empty for the screen itself.
 * @param query - The query parameters.
 * @returns The address.
 */
function databasePath(engine: string, server: string, path: string, query: Record<string, string | undefined>): string {
  const params = new URLSearchParams();

  for (const [name, value] of Object.entries(query)) {
    if (value) {
      params.set(name, value);
    }
  }

  const search = params.toString();

  return `/api/v1/metrics/databases/${encodeURIComponent(engine)}/${encodeURIComponent(server)}${path}${
    search ? `?${search}` : ""
  }`;
}

/**
 * The Simple view as a query parameter (VEY-67): sent only when on, since the server then keeps to the rows the Simple
 * view shows.
 */
function simpleParam(simple: boolean): Record<string, string | undefined> {
  return { simple: simple ? "true" : undefined };
}

/** The System choice as a query parameter (VEY-66): sent only when on, since the server leaves its own rows out. */
function systemParam(system: boolean): Record<string, string | undefined> {
  return { system: system ? "true" : undefined };
}

/** The Veyra REST API, as documented in docs/api/rest.md. */
/** The query string of a resource list: the window, the environment, the filters, and the page. */
function resourceParams(query: ResourceQuery): string {
  const params = new URLSearchParams({ from: query.from, to: query.to });

  if (query.environment) {
    params.set("environment", query.environment);
  }

  for (const [key, value] of Object.entries(query.filters)) {
    if (value) {
      params.set(key, value);
    }
  }

  if (query.cursor) {
    params.set("cursor", query.cursor);
  }

  return params.toString();
}

export const api = {
  /** Whether first-run setup is still needed. */
  setupStatus: () => request<{ required: boolean }>("/api/v1/setup"),

  /** Creates the first administrator with the token from the setup link. */
  completeSetup: (setup: { token: string; email: string; displayName: string; password: string }) =>
    request<undefined>("/api/v1/setup", { method: "POST", body: setup }),

  /**
   * The password step of signing in. An account with a second factor, or one that must set one up, answers with the
   * next step instead of a session (ADR 0011).
   */
  async signIn(email: string, password: string): Promise<PasswordStep> {
    const answer = await request<Session | { next: SignInNext }>("/api/v1/session", {
      method: "POST",
      body: { email, password },
    });

    if ("next" in answer) {
      return { next: answer.next };
    }

    await refreshAntiforgery();

    return { session: answer };
  },

  /** The code step of signing in: a code from the app, or a recovery code. */
  async signInWithCode(code: string): Promise<Session> {
    const session = await request<Session>("/api/v1/session/second-factor", { method: "POST", body: { code } });
    await refreshAntiforgery();

    return session;
  },

  /** Starts setting up a second factor that an Admin requires, right after the password step. */
  startRequiredSetup: () => request<SecondFactorSetup>("/api/v1/session/second-factor/setup", { method: "POST" }),

  /** Confirms the required second factor and starts the session. */
  async confirmRequiredSetup(code: string): Promise<{ session: Session; recoveryCodes: string[] }> {
    const answer = await request<{ session: Session; recoveryCodes: string[] }>(
      "/api/v1/session/second-factor/setup/confirm",
      { method: "POST", body: { code } },
    );
    await refreshAntiforgery();

    return answer;
  },

  /** The signed-in user, or `null` when nobody is signed in. */
  async currentSession(): Promise<Session | null> {
    try {
      const session = await request<Session>("/api/v1/session");
      await refreshAntiforgery();

      return session;
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        return null;
      }

      throw error;
    }
  },

  /** Signs out and forgets the antiforgery token. */
  async signOut(): Promise<void> {
    await request<undefined>("/api/v1/session", { method: "DELETE" });
    antiforgeryToken = null;
  },

  /** One page of log records. */
  searchLogs: (params: URLSearchParams, signal?: AbortSignal) =>
    request<LogsPage>(`/api/v1/logs?${params.toString()}`, { signal }),

  /** The terms of a log query as the server parses them, with where each one sits in the text (ADR 0024). */
  parseLogQuery: (query: string, signal?: AbortSignal) =>
    request<LogQueryParse>(`/api/v1/logs/parse?${new URLSearchParams({ q: query }).toString()}`, { signal }),

  /**
   * The address that exports every record the filters match, not only the loaded page (ADR 0024). The browser follows
   * it as a download, so the server streams the file and nothing is held in memory here.
   */
  logExportUrl: (params: URLSearchParams, format: "ndjson" | "csv") => {
    const query = new URLSearchParams(params);
    query.delete("limit");
    query.delete("cursor");
    query.set("format", format);

    return `/api/v1/logs/export?${query.toString()}`;
  },

  /** The saved views of a screen: the person's own and those others shared (ADR 0024). */
  savedViews: (screen: string, signal?: AbortSignal) =>
    request<{ items: SavedView[] }>(`/api/v1/views?${new URLSearchParams({ screen }).toString()}`, { signal }).then(
      (answer) => answer.items,
    ),

  /** Saves a new view. */
  createView: (view: SavedViewInput) => request<SavedView>("/api/v1/views", { method: "POST", body: view }),

  /** Changes a view of one's own. */
  updateView: (id: string, view: SavedViewInput) =>
    request<SavedView>(`/api/v1/views/${encodeURIComponent(id)}`, { method: "PUT", body: view }),

  /** Deletes a view of one's own. */
  deleteView: (id: string) => request<undefined>(`/api/v1/views/${encodeURIComponent(id)}`, { method: "DELETE" }),

  /** Counts per time bucket and per service, for the histogram and the service filter. */
  summarizeLogs: (params: URLSearchParams, signal?: AbortSignal) =>
    request<LogSummary>(`/api/v1/logs/summary?${params.toString()}`, { signal }),

  /** One page of the trace list, newest first (ADR 0014). */
  listTraces: (params: URLSearchParams, signal?: AbortSignal) =>
    request<TracesPage>(`/api/v1/traces?${params.toString()}`, { signal }),

  /** The durations of the newest traces of the window, for the list's chart. */
  traceDurations: (params: URLSearchParams, signal?: AbortSignal) =>
    request<TraceDurations>(`/api/v1/traces/durations?${params.toString()}`, { signal }),

  /**
   * The services that received requests in the window, with their p95 and failed traces: most traces first, or with
   * `order=slowest` the slowest p95 first (ADR 0029).
   */
  traceServices: (params: URLSearchParams, signal?: AbortSignal) =>
    request<TraceService[]>(`/api/v1/traces/services?${params.toString()}`, { signal }),

  /** The figures of the window's traces under the same filters as the list (ADR 0029). */
  traceSummary: (params: URLSearchParams, signal?: AbortSignal) =>
    request<TracesSummary>(`/api/v1/traces/summary?${params.toString()}`, { signal }),

  /**
   * One trace with the spans that have arrived and the figures of all of them; `at` is when it is known to have
   * happened, and `errorsOnly` keeps the failed spans and the spans above them (VEY-67). 404 when unknown.
   */
  trace: (traceId: string, at: string | null, errorsOnly: boolean, signal?: AbortSignal) => {
    const params = new URLSearchParams();

    if (at) {
      params.set("at", at);
    }

    if (errorsOnly) {
      params.set("errorsOnly", "true");
    }

    const query = params.toString();

    return request<Trace>(`/api/v1/traces/${encodeURIComponent(traceId)}${query ? `?${query}` : ""}`, { signal });
  },

  /** The home snapshot of one environment, or of all of them when `environment` is null. */
  home: (environment: string | null, signal?: AbortSignal) =>
    request<HomeSnapshot>(
      environment ? `/api/v1/home?environment=${encodeURIComponent(environment)}` : "/api/v1/home",
      { signal },
    ),

  /** The environment switcher's list for what its find box holds, filtered by the server (VEY-67). */
  homeEnvironments: (search: string, signal?: AbortSignal) =>
    request<EnvironmentChoices>(
      search.trim()
        ? `/api/v1/home/environments?search=${encodeURIComponent(search.trim())}`
        : "/api/v1/home/environments",
      { signal },
    ),

  /** All ingestion keys, newest first, without secrets. */
  listKeys: () => request<IngestionKey[]>("/api/v1/ingestion-keys"),

  /** Creates an ingestion key; the answer holds its secret once. */
  createKey: (key: { name: string; environments: string[] | null; expiresAt: string | null }) =>
    request<CreatedIngestionKey>("/api/v1/ingestion-keys", { method: "POST", body: key }),

  /** Revokes an ingestion key. */
  revokeKey: (id: string) =>
    request<undefined>(`/api/v1/ingestion-keys/${encodeURIComponent(id)}`, { method: "DELETE" }),

  /** The hosts, services, database servers, and containers that sent metrics recently and match the choices. */
  metricsOverview: (range: MetricRange, choices: MetricsOverviewChoices, signal?: AbortSignal) => {
    const params = new URLSearchParams(rangeParams(range));

    if (choices.search.trim()) {
      params.set("search", choices.search.trim());
    }

    if (choices.environment) {
      params.set("environment", choices.environment);
    }

    return request<MetricsOverview>(`/api/v1/metrics/overview?${params.toString()}`, { signal });
  },

  /** One host's metrics; 404 when it sent none within the range. */
  hostMetrics: (host: string, range: MetricRange, signal?: AbortSignal) =>
    request<HostMetrics>(
      `/api/v1/metrics/hosts/${encodeURIComponent(host)}?${new URLSearchParams(rangeParams(range)).toString()}`,
      { signal },
    ),

  /**
   * One database server's metrics (ADR 0018); 404 when it sent none within the range. The server leaves its own
   * databases, jobs, statements, and tables out unless `system` is true (VEY-66).
   */
  databaseMetrics: (engine: string, server: string, range: MetricRange, system: boolean, signal?: AbortSignal) =>
    request<DatabaseMetrics>(databasePath(engine, server, "", { ...rangeParams(range), ...systemParam(system) }), {
      signal,
    }),

  /**
   * A database server's Live tab (ADR 0020): its newest read, or the read at a moment; 404 when it sent nothing. The
   * server lists only the sessions that meet the filters (VEY-66).
   */
  databaseLive: (engine: string, server: string, at: string | null, filters: LiveFilterValues, signal?: AbortSignal) =>
    request<DatabaseLive>(databasePath(engine, server, "/live", { ...filters, ...(at ? { at } : {}) }), { signal }),

  /** A database server's statements over the range (ADR 0020), ranked by the server by `sort` (VEY-66). */
  databaseQueries: (
    engine: string,
    server: string,
    range: MetricRange,
    system: boolean,
    sort: QuerySort,
    simple: boolean,
    signal?: AbortSignal,
  ) =>
    request<DatabaseQueries>(
      databasePath(engine, server, "/queries", {
        ...rangeParams(range),
        ...systemParam(system),
        ...(sort === "totalTime" ? {} : { sort }),
        ...simpleParam(simple),
      }),
      { signal },
    ),

  /** One statement's page (ADR 0020); 404 when it did not run in the range. */
  databaseQuery: (engine: string, server: string, hash: string, range: MetricRange, signal?: AbortSignal) =>
    request<DatabaseQuery>(
      `/api/v1/metrics/databases/${encodeURIComponent(engine)}/${encodeURIComponent(server)}/queries/${encodeURIComponent(
        hash,
      )}?${new URLSearchParams(rangeParams(range)).toString()}`,
      { signal },
    ),

  /** A database server's blocking episodes and deadlocks over the range (ADR 0020). */
  databaseBlocking: (engine: string, server: string, range: MetricRange, signal?: AbortSignal) =>
    request<DatabaseBlocking>(databasePath(engine, server, "/blocking", rangeParams(range)), { signal }),

  /**
   * A database server's databases and volumes in full (ADR 0020); 404 before its first storage read. Its own
   * databases are left out unless `system` is true (VEY-66).
   */
  /** A SQL Server's overview in one read: machine, figures, performance, storage, blocking, coverage (ADR 0025). */
  databaseOverview: (engine: string, server: string, range: MetricRange, system: boolean, signal?: AbortSignal) =>
    request<ServerOverview>(
      databasePath(engine, server, "/overview", { ...rangeParams(range), ...systemParam(system) }),
      {
        signal,
      },
    ),

  /** A SQL Server's backups of the range, per day, the last 7 days, and freshness per database (ADR 0025). */
  databaseBackups: (engine: string, server: string, range: MetricRange, system: boolean, signal?: AbortSignal) =>
    request<ServerBackups>(
      databasePath(engine, server, "/backups", { ...rangeParams(range), ...systemParam(system) }),
      {
        signal,
      },
    ),

  databaseStorage: (engine: string, server: string, system: boolean, signal?: AbortSignal) =>
    request<StorageView>(databasePath(engine, server, "/storage", systemParam(system)), { signal }),

  /** A database server's index and statistics advice (ADR 0020); its own databases only when `system` is true. */
  databaseIndexes: (engine: string, server: string, system: boolean, simple: boolean, signal?: AbortSignal) =>
    request<IndexAdvice>(databasePath(engine, server, "/indexes", { ...systemParam(system), ...simpleParam(simple) }), {
      signal,
    }),

  /** A database server as a whole over the range (ADR 0020); its idle waits only when `system` is true. */
  databaseServer: (engine: string, server: string, range: MetricRange, system: boolean, signal?: AbortSignal) =>
    request<ServerView>(databasePath(engine, server, "/server", { ...rangeParams(range), ...systemParam(system) }), {
      signal,
    }),

  /** A database server's SQL Agent jobs with their runs and steps (ADR 0020); its own only when `system` is true. */
  databaseJobs: (engine: string, server: string, system: boolean, simple: boolean, signal?: AbortSignal) =>
    request<AgentJobsView>(databasePath(engine, server, "/jobs", { ...systemParam(system), ...simpleParam(simple) }), {
      signal,
    }),

  /** A database server's events from system_health over the range (ADR 0020). */
  databaseEvents: (engine: string, server: string, range: MetricRange, simple: boolean, signal?: AbortSignal) =>
    request<ServerEvent[]>(databasePath(engine, server, "/events", { ...rangeParams(range), ...simpleParam(simple) }), {
      signal,
    }),

  /** A database server's Availability Groups, log shipping, and cluster (ADR 0020). */
  databaseAvailability: (engine: string, server: string, signal?: AbortSignal) =>
    request<AvailabilityView>(
      `/api/v1/metrics/databases/${encodeURIComponent(engine)}/${encodeURIComponent(server)}/availability`,
      { signal },
    ),

  /** A database server's settings, security posture, and changes (ADR 0020). */
  databaseConfiguration: (engine: string, server: string, signal?: AbortSignal) =>
    request<ConfigurationView>(
      `/api/v1/metrics/databases/${encodeURIComponent(engine)}/${encodeURIComponent(server)}/configuration`,
      { signal },
    ),

  /** What the monitoring login lacks on a database server, and the statements that grant it (VEY-58). */
  databasePermissions: (engine: string, server: string, signal?: AbortSignal) =>
    request<PermissionsView>(
      `/api/v1/metrics/databases/${encodeURIComponent(engine)}/${encodeURIComponent(server)}/permissions`,
      { signal },
    ),

  /** The catalog rules with the words their alerts use (ADR 0020). */
  alertCatalog: (signal?: AbortSignal) => request<CatalogRule[]>("/api/v1/alerts/catalog", { signal }),

  /** One service's metrics; 404 when it sent none within the range. */
  serviceMetrics: (service: string, environment: string | null, range: MetricRange, signal?: AbortSignal) => {
    const params = new URLSearchParams(rangeParams(range));

    if (environment) {
      params.set("environment", environment);
    }

    return request<ServiceMetrics>(`/api/v1/metrics/services/${encodeURIComponent(service)}?${params.toString()}`, {
      signal,
    });
  },

  /** The metric names seen within the range. */
  metricNames: (search: string, range: MetricRange, signal?: AbortSignal) => {
    const params = new URLSearchParams(rangeParams(range));

    if (search.trim()) {
      params.set("search", search.trim());
    }

    return request<MetricNameInfo[]>(`/api/v1/metrics/names?${params.toString()}`, { signal });
  },

  /** One metric charted, as one line or split by an attribute, for one service or host when they are chosen. */
  metricChart: (
    query: {
      name: string;
      show: string | null;
      split: string | null;
      service: string | null;
      host: string | null;
      range: MetricRange;
    },
    signal?: AbortSignal,
  ) => {
    const params = new URLSearchParams({ name: query.name, ...rangeParams(query.range) });

    if (query.show) {
      params.set("show", query.show);
    }

    if (query.split) {
      params.set("split", query.split);
    }

    // The server keeps the series of the chosen service and host (ADR 0029).
    if (query.service) {
      params.set("service", query.service);
    }

    if (query.host) {
      params.set("host", query.host);
    }

    return request<MetricChart>(`/api/v1/metrics/chart?${params.toString()}`, { signal });
  },

  /** Incidents, open first, then newest (ADR 0026). */
  incidents: (
    state: IncidentState | "all",
    days: number,
    environment: string | null,
    cursor: string | null,
    signal?: AbortSignal,
  ) => {
    const params = new URLSearchParams({ state, days: String(days), limit: "50" });

    if (environment) {
      params.set("environment", environment);
    }

    if (cursor) {
      params.set("cursor", cursor);
    }

    return request<IncidentPage>(`/api/v1/incidents?${params.toString()}`, { signal });
  },

  /** One incident with its alerts, figures, timeline, and runbooks (ADR 0026). */
  incident: (id: string, signal?: AbortSignal) =>
    request<IncidentDetails>(`/api/v1/incidents/${encodeURIComponent(id)}`, { signal }),

  /** What surrounds an incident: execution path, exception, signals, charts, logs, and components (ADR 0026). */
  incidentContext: (id: string, signal?: AbortSignal) =>
    request<IncidentContext>(`/api/v1/incidents/${encodeURIComponent(id)}/context`, { signal }),

  /** Acknowledges an incident; Editors and Admins only. */
  acknowledgeIncident: (id: string) =>
    request<IncidentItem>(`/api/v1/incidents/${encodeURIComponent(id)}/acknowledge`, { method: "POST" }),

  /** Takes an acknowledgement back. */
  unacknowledgeIncident: (id: string) =>
    request<IncidentItem>(`/api/v1/incidents/${encodeURIComponent(id)}/acknowledge`, { method: "DELETE" }),

  /** The jobs of a window, failing ones first, filtered and counted on the server (ADR 0027). */
  jobs: (query: JobQuery, signal?: AbortSignal) => {
    const params = new URLSearchParams({ from: query.from, to: query.to });

    if (query.environment) {
      params.set("environment", query.environment);
    }

    if (query.source) {
      params.set("source", query.source);
    }

    if (query.q.trim()) {
      params.set("q", query.q.trim());
    }

    if (query.status !== "all") {
      params.set("status", query.status);
    }

    return request<JobList>(`/api/v1/jobs?${params.toString()}`, { signal });
  },

  /** One job over a window: its figures, run durations, and runs with their steps (ADR 0027); 404 for an unknown ID. */
  job: (id: string, window: { from: string; to: string }, signal?: AbortSignal) =>
    request<JobDetails>(`/api/v1/jobs/${encodeURIComponent(id)}?${new URLSearchParams(window).toString()}`, {
      signal,
    }),

  /** One alert with its timeline and the measure of its rule. */
  alert: (id: string, signal?: AbortSignal) =>
    request<AlertDetails>(`/api/v1/alerts/${encodeURIComponent(id)}`, { signal }),

  /** What surrounds an alert: the blocks it touches with their state, and the logs around it (ADR 0029). */
  alertContext: (id: string, signal?: AbortSignal) =>
    request<AlertContext>(`/api/v1/alerts/${encodeURIComponent(id)}/context`, { signal }),

  /** One page of the alerts of the last days, newest first. */
  alerts: (query: AlertQuery, cursor: string | null, signal?: AbortSignal) => {
    const params = new URLSearchParams({ days: String(query.days), timeZone: displayZone() });

    if (query.state !== "all") {
      params.set("state", query.state);
    }

    for (const rule of query.rules) {
      params.append("rule", rule);
    }

    if (query.text.trim()) {
      params.set("q", query.text.trim());
    }

    if (query.environment) {
      params.set("environment", query.environment);
    }

    // A block's own screen asks for exactly its alerts, so the server keeps them before the page's limit (VEY-68).
    for (const key of ["kind", "block", "part", "order"] as const) {
      const value = query[key];

      if (value) {
        params.set(key, value);
      }
    }

    if (query.limit !== undefined) {
      params.set("limit", String(query.limit));
    }

    if (cursor) {
      params.set("cursor", cursor);
    }

    return request<AlertList>(`/api/v1/alerts?${params.toString()}`, { signal });
  },

  /** How each area of a database server is from its open alerts, the worst first (VEY-68). */
  alertAreas: (block: string, areas: string[], signal?: AbortSignal) => {
    const params = new URLSearchParams({ block });

    for (const area of areas) {
      params.append("area", area);
    }

    return request<AlertAreaState[]>(`/api/v1/alerts/areas?${params.toString()}`, { signal });
  },

  /** The counts of the alerts of the last days, of one environment or of all when it is null. */
  alertSummary: (days: number, environment: string | null, signal?: AbortSignal) => {
    // Days start at midnight in the zone the person sees times in (VEY-22).
    const params = new URLSearchParams({ days: String(days), timeZone: displayZone() });

    if (environment) {
      params.set("environment", environment);
    }

    return request<AlertSummary>(`/api/v1/alerts/summary?${params.toString()}`, { signal });
  },

  /** The email server and every notification channel, with recent activity. */
  listChannels: () =>
    request<{ emailServer: EmailServer; channels: NotificationChannel[] }>("/api/v1/notification-channels"),

  /** Adds a notification channel. */
  createChannel: (input: ChannelInput) =>
    request<NotificationChannel>("/api/v1/notification-channels", { method: "POST", body: input }),

  /** Changes a notification channel; a webhook without a URL keeps the stored one. */
  updateChannel: (id: string, input: ChannelInput) =>
    request<NotificationChannel>(`/api/v1/notification-channels/${encodeURIComponent(id)}`, {
      method: "PUT",
      body: input,
    }),

  /** Deletes a notification channel and its history. */
  deleteChannel: (id: string) =>
    request<undefined>(`/api/v1/notification-channels/${encodeURIComponent(id)}`, { method: "DELETE" }),

  /** Sends a test message to a stored channel. */
  testChannel: (id: string) =>
    request<SendResult>(`/api/v1/notification-channels/${encodeURIComponent(id)}/test`, { method: "POST" }),

  /** The signed-in person's own account, sessions, and latest sign-ins. */
  account: (signal?: AbortSignal) => request<Account>("/api/v1/account", { signal }),

  /** Changes the signed-in person's password; every other browser is signed out. */
  changePassword: (currentPassword: string, newPassword: string) =>
    request<undefined>("/api/v1/session/password", { method: "PUT", body: { currentPassword, newPassword } }),

  /** Starts setting up a second factor on the Account page. */
  startSecondFactor: () => request<SecondFactorSetup>("/api/v1/account/second-factor", { method: "POST" }),

  /** Switches the second factor on with a working code; answers with the recovery codes. */
  confirmSecondFactor: (code: string) =>
    request<{ recoveryCodes: string[] }>("/api/v1/account/second-factor/confirm", { method: "POST", body: { code } }),

  /** Replaces the recovery codes; needs the password again. */
  newRecoveryCodes: (password: string) =>
    request<{ recoveryCodes: string[] }>("/api/v1/account/second-factor/recovery-codes", {
      method: "POST",
      body: { password },
    }),

  /** Switches the second factor off; needs the password again. */
  switchOffSecondFactor: (password: string) =>
    request<undefined>("/api/v1/account/second-factor/off", { method: "POST", body: { password } }),

  /** Ends one of the signed-in person's sessions. */
  endOwnSession: (id: string) =>
    request<undefined>(`/api/v1/account/sessions/${encodeURIComponent(id)}`, { method: "DELETE" }),

  /** Ends every other session of the signed-in person. */
  endOtherSessions: () => request<{ ended: number }>("/api/v1/account/sessions/end-others", { method: "POST" }),

  /** Keeps the time zone every screen shows the person's times in, or clears it with null for the browser's. */
  setTimeZone: (timeZone: string | null) =>
    request<undefined>("/api/v1/account/time-zone", { method: "PUT", body: { timeZone } }),

  /** Searches services, hosts, databases, traces, and logs at once; the server matches and ranks (ADR 0022). */
  search: (query: string, environment: string | null, window: { from: string; to: string }, signal?: AbortSignal) => {
    const params = new URLSearchParams({ q: query, from: window.from, to: window.to });

    if (environment) {
      params.set("environment", environment);
    }

    return request<SearchResults>(`/api/v1/search?${params.toString()}`, { signal });
  },

  /**
   * The Overview: the whole environment in one answer, every figure worked out on the server (ADR 0023), with as many
   * databases and pipelines as fit in their tiles (VEY-81); the server's own numbers when left out.
   */
  overview: (
    window: { from: string; to: string },
    environment: string | null,
    signal?: AbortSignal,
    rows?: { databases: number; pipelines: number },
  ) => {
    const params = new URLSearchParams(window);

    if (environment) {
      params.set("environment", environment);
    }

    if (rows) {
      params.set("databases", String(rows.databases));
      params.set("pipelines", String(rows.pipelines));
    }

    return request<Overview>(`/api/v1/overview?${params.toString()}`, { signal });
  },

  /** One page of the Services list, filtered, counted, and ranked on the server (ADR 0022). */
  resourceServices: (query: ResourceQuery, signal?: AbortSignal) =>
    request<ResourcePage<ServicesHeader, ServiceRow>>(`/api/v1/resources/services?${resourceParams(query)}`, {
      signal,
    }),

  /** One page of the Hosts & containers list, with silent ones still listed (ADR 0022). */
  resourceHosts: (query: ResourceQuery, signal?: AbortSignal) =>
    request<ResourcePage<HostsHeader, HostRow>>(`/api/v1/resources/hosts?${resourceParams(query)}`, { signal }),

  /** One page of the Databases list (ADR 0022). */
  resourceDatabases: (query: ResourceQuery, signal?: AbortSignal) =>
    request<ResourcePage<DatabasesHeader, DatabaseServerRow>>(`/api/v1/resources/databases?${resourceParams(query)}`, {
      signal,
    }),

  /** Keeps the look the person picked, or clears it with null for the instance's default (VEY-71). */
  setLook: (look: LookId | null) => request<undefined>("/api/v1/account/look", { method: "PUT", body: { look } }),

  /** The people a search and a chip keep, with every figure the Users screen shows (VEY-67). */
  listUsers: (choices: UserListChoices, signal?: AbortSignal) => {
    const params = new URLSearchParams();

    if (choices.search.trim()) {
      params.set("search", choices.search.trim());
    }

    if (choices.show !== "everyone") {
      params.set("show", choices.show);
    }

    const query = params.toString();

    return request<UserList>(`/api/v1/users${query ? `?${query}` : ""}`, { signal });
  },

  /** One person in full. */
  user: (id: string, signal?: AbortSignal) =>
    // The server counts the sign-ins per day on the calendar of the zone the person sees times in (VEY-68).
    request<UserDetail>(`/api/v1/users/${encodeURIComponent(id)}?timeZone=${encodeURIComponent(displayZone())}`, {
      signal,
    }),

  /** Adds a person. */
  createUser: (input: NewUser) => request<UserSummary>("/api/v1/users", { method: "POST", body: input }),

  /** Changes a person's role, team, or whether they can sign in. */
  updateUser: (id: string, changes: UserChanges) =>
    request<UserSummary>(`/api/v1/users/${encodeURIComponent(id)}`, { method: "PATCH", body: changes }),

  /** Sets a new password for a person; lifts a lock and signs them out everywhere. */
  resetPassword: (id: string, password: string) =>
    request<undefined>(`/api/v1/users/${encodeURIComponent(id)}/password`, { method: "POST", body: { password } }),

  /** Removes a person's second factor, for a lost phone. */
  removeSecondFactor: (id: string) =>
    request<undefined>(`/api/v1/users/${encodeURIComponent(id)}/second-factor`, { method: "DELETE" }),

  /** Ends one of a person's sessions. */
  endUserSession: (id: string, sessionId: string) =>
    request<undefined>(`/api/v1/users/${encodeURIComponent(id)}/sessions/${encodeURIComponent(sessionId)}`, {
      method: "DELETE",
    }),

  /** Signs a person out everywhere. */
  endUserSessions: (id: string) =>
    request<{ ended: number }>(`/api/v1/users/${encodeURIComponent(id)}/sessions`, { method: "DELETE" }),

  /** Who must sign in with a second factor. */
  secondFactorPolicy: () => request<{ requiredFor: SecondFactorPolicy }>("/api/v1/users/second-factor-policy"),

  /** Sets who must sign in with a second factor. */
  setSecondFactorPolicy: (requiredFor: SecondFactorPolicy) =>
    request<{ requiredFor: SecondFactorPolicy }>("/api/v1/users/second-factor-policy", {
      method: "PUT",
      body: { requiredFor },
    }),

  /** Every setting, with its value, default, and who saved it (ADR 0017). */
  settings: (signal?: AbortSignal) => request<SettingsPage>("/api/v1/settings", { signal }),

  /** Which settings and sections the screen lists for a search or a section in a view (VEY-67). */
  settingsListing: (choices: SettingsListingChoices, signal?: AbortSignal) => {
    const params = new URLSearchParams({ section: choices.section, view: choices.view });

    if (choices.search.trim()) {
      params.set("search", choices.search.trim());
    }

    return request<SettingsListing>(`/api/v1/settings/listing?${params.toString()}`, { signal });
  },

  /** Checks changes and says what they would do, without saving anything. */
  reviewSettings: (version: number, changes: SettingChangeInput[]) =>
    request<SettingsReview>("/api/v1/settings/review", { method: "POST", body: { version, changes } }),

  /**
   * Saves changes, all or nothing. Refused with 400 and the review in the error's body, or 409 when someone else saved
   * in between.
   */
  saveSettings: (version: number, changes: SettingChangeInput[], confirmed: boolean, origin: string | null = null) =>
    request<SettingsPage>("/api/v1/settings", { method: "PUT", body: { version, changes, confirmed, origin } }),

  /** Deletes all telemetry, once the person typed the site's host name (VEY-59). Admins only. */
  clearTelemetry: (confirm: string) =>
    request<undefined>("/api/v1/settings/clear-telemetry", { method: "POST", body: { confirm } }),

  /** A page of the settings history, newest first. */
  settingsHistory: (params: URLSearchParams, signal?: AbortSignal) =>
    request<SettingsHistory>(`/api/v1/settings/history?${params}`, { signal }),

  /** Sets a setting back to the value it had before a change, as a new change. */
  revertSetting: (id: number, version: number, confirmed: boolean) =>
    request<SettingsPage>(`/api/v1/settings/history/${id}/revert`, { method: "POST", body: { version, confirmed } }),

  /** Every saved value except secrets, as a file. */
  exportSettings: () => request<SettingsFile>("/api/v1/settings/export"),

  /** Reviews what importing a file would change; nothing is saved. */
  importSettings: (file: SettingsFile) =>
    request<SettingsReview>("/api/v1/settings/import", { method: "POST", body: file }),

  /** Sends a test message to a channel as entered; with an ID, a webhook without a URL uses the stored one. */
  testDraft: (input: ChannelInput, id: string | null) =>
    request<SendResult>(
      id ? `/api/v1/notification-channels/test?id=${encodeURIComponent(id)}` : "/api/v1/notification-channels/test",
      { method: "POST", body: input },
    ),

  /** The version and the build that answer, which the shell compares with its own to offer a reload (VEY-82). */
  about: (signal?: AbortSignal) => request<About>("/api/v1/about", { signal }),

  /** The release notes, newest first (VEY-82). */
  releases: (signal?: AbortSignal) => request<Release[]>("/api/v1/about/releases", { signal }),

  /** Veyra's agents, the built-in one first (ADR 0019). */
  agents: (signal?: AbortSignal) => request<Agent[]>("/api/v1/agents", { signal }),

  /** How many servers and agents there are and how they stand, for the Settings list (VEY-68). */
  agentsSummary: (signal?: AbortSignal) => request<AgentsSummary>("/api/v1/agents/summary", { signal }),

  /** Adds an agent for a network Veyra cannot reach; the answer holds its one-time code, shown once. */
  addAgent: (name: string) => request<AddedAgent>("/api/v1/agents", { method: "POST", body: { name } }),

  /** A new one-time code for an agent in another network, to install it for the first time or again. */
  newAgentCode: (id: string) => request<AgentCode>(`/api/v1/agents/${encodeURIComponent(id)}/code`, { method: "POST" }),

  /** Removes an agent in another network and revokes its key. */
  deleteAgent: (id: string) => request<undefined>(`/api/v1/agents/${encodeURIComponent(id)}`, { method: "DELETE" }),

  /** Sets what an installed agent reads of its own host; answers with the agent, or 400 with the reason. */
  setAgentHost: (id: string, input: AgentHostInput) =>
    request<Agent>(`/api/v1/agents/${encodeURIComponent(id)}/host`, { method: "PUT", body: input }),

  /** The SQL Servers Veyra's agents read, by name, each with its state. */
  sqlServers: (signal?: AbortSignal) => request<SqlServer[]>("/api/v1/sql-servers", { signal }),

  /** Adds a SQL Server; its agent starts reading it within 30 seconds. */
  createSqlServer: (input: SqlServerInput) =>
    request<SqlServer>("/api/v1/sql-servers", { method: "POST", body: input }),

  /** Changes a SQL Server; a null password keeps the stored one. */
  updateSqlServer: (id: string, input: SqlServerInput) =>
    request<SqlServer>(`/api/v1/sql-servers/${encodeURIComponent(id)}`, { method: "PUT", body: input }),

  /** Removes a SQL Server; the data it sent stays until retention removes it. */
  deleteSqlServer: (id: string) =>
    request<undefined>(`/api/v1/sql-servers/${encodeURIComponent(id)}`, { method: "DELETE" }),

  /** The integrations: each monitored SQL Server with its template version and modules (ADR 0028). */
  integrations: (signal?: AbortSignal) => request<IntegrationList>("/api/v1/integrations", { signal }),

  /** The SQL Server template's modules with their default intervals and choices (ADR 0028). */
  integrationModules: (signal?: AbortSignal) =>
    request<IntegrationModuleInfo[]>("/api/v1/integrations/modules/sqlserver", { signal }),

  /** Starts a probe through the agent: an unsaved connection, or a saved server by its ID (ADR 0028). */
  startProbe: (input: ProbeInput) =>
    request<{ id: string }>("/api/v1/integrations/probes", { method: "POST", body: input }),

  /** Where a probe stands: pending, its result, or why it failed. */
  probe: (id: string, signal?: AbortSignal) =>
    request<Probe>(`/api/v1/integrations/probes/${encodeURIComponent(id)}`, { signal }),

  /** One SQL Server integration's package page over a window (ADR 0028). */
  sqlServerIntegration: (id: string, window: { from: string; to: string }, signal?: AbortSignal) =>
    request<IntegrationDetails>(
      `/api/v1/integrations/sqlserver/${encodeURIComponent(id)}?${new URLSearchParams(window).toString()}`,
      { signal },
    ),

  /** Moves an integration to another template version: Update to the current one, or Roll back to the previous. */
  setIntegrationVersion: (id: string, version: string) =>
    request<IntegrationItem>(`/api/v1/integrations/sqlserver/${encodeURIComponent(id)}/version`, {
      method: "POST",
      body: { version },
    }),

  /** The PostgreSQL servers Veyra's agents read, by name, each with its state. */
  postgresServers: (signal?: AbortSignal) => request<PostgresServer[]>("/api/v1/postgres-servers", { signal }),

  /** Adds a PostgreSQL server; its agent starts reading it within 30 seconds. */
  createPostgresServer: (input: PostgresServerInput) =>
    request<PostgresServer>("/api/v1/postgres-servers", { method: "POST", body: input }),

  /** Changes a PostgreSQL server; a null password keeps the stored one. */
  updatePostgresServer: (id: string, input: PostgresServerInput) =>
    request<PostgresServer>(`/api/v1/postgres-servers/${encodeURIComponent(id)}`, { method: "PUT", body: input }),

  /** Removes a PostgreSQL server; the data it sent stays until retention removes it. */
  deletePostgresServer: (id: string) =>
    request<undefined>(`/api/v1/postgres-servers/${encodeURIComponent(id)}`, { method: "DELETE" }),
};
