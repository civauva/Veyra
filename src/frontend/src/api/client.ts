import type {
  Account,
  AlertDetails,
  AlertList,
  AlertQuery,
  AlertSummary,
  ChannelInput,
  CreatedIngestionKey,
  EmailServer,
  HostMetrics,
  HomeSnapshot,
  IngestionKey,
  LogsPage,
  LogSummary,
  MetricChart,
  MetricNameInfo,
  MetricRange,
  MetricsOverview,
  NewUser,
  NotificationChannel,
  PasswordStep,
  SecondFactorPolicy,
  SecondFactorSetup,
  SendResult,
  ServiceMetrics,
  Session,
  SignInNext,
  UserChanges,
  UserDetail,
  UserSummary,
} from "./types";

/**
 * A request the Veyra API refused, with the title of its problem details (RFC 9457) as the message.
 */
export class ApiError extends Error {
  /**
   * @param status - The HTTP status code.
   * @param message - A message that can be shown to the person.
   */
  constructor(
    public readonly status: number,
    message: string,
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
    throw new ApiError(response.status, await problemTitle(response));
  }

  const text = await response.text();

  return (text ? JSON.parse(text) : undefined) as T;
}

/** Reads the title of a problem details answer, with a fallback for answers that have none. */
async function problemTitle(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as { title?: unknown };

    if (typeof problem.title === "string" && problem.title.length > 0) {
      return problem.title;
    }
  } catch {
    // Not JSON: fall through to the generic message.
  }

  return `Veyra answered with status ${response.status}.`;
}

/** Fetches a fresh antiforgery token. Needed after every sign-in, because the token is bound to the session. */
async function refreshAntiforgery(): Promise<void> {
  const { token } = await request<{ token: string }>("/api/v1/session/antiforgery");

  antiforgeryToken = token;
}

/** The Veyra REST API, as documented in docs/api/rest.md. */
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

  /** Counts per time bucket and per service, for the histogram and the service filter. */
  summarizeLogs: (params: URLSearchParams, signal?: AbortSignal) =>
    request<LogSummary>(`/api/v1/logs/summary?${params.toString()}`, { signal }),

  /** The home snapshot of one environment, or of all of them when `environment` is null. */
  home: (environment: string | null, signal?: AbortSignal) =>
    request<HomeSnapshot>(
      environment ? `/api/v1/home?environment=${encodeURIComponent(environment)}` : "/api/v1/home",
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

  /** Every host and service that sent metrics recently. */
  metricsOverview: (range: MetricRange, signal?: AbortSignal) =>
    request<MetricsOverview>(`/api/v1/metrics/overview?range=${range}`, { signal }),

  /** One host's metrics; 404 when it sent none within the range. */
  hostMetrics: (host: string, range: MetricRange, signal?: AbortSignal) =>
    request<HostMetrics>(`/api/v1/metrics/hosts/${encodeURIComponent(host)}?range=${range}`, { signal }),

  /** One service's metrics; 404 when it sent none within the range. */
  serviceMetrics: (service: string, environment: string | null, range: MetricRange, signal?: AbortSignal) => {
    const params = new URLSearchParams({ range });

    if (environment) {
      params.set("environment", environment);
    }

    return request<ServiceMetrics>(`/api/v1/metrics/services/${encodeURIComponent(service)}?${params.toString()}`, {
      signal,
    });
  },

  /** The metric names seen within the range. */
  metricNames: (search: string, range: MetricRange, signal?: AbortSignal) => {
    const params = new URLSearchParams({ range });

    if (search.trim()) {
      params.set("search", search.trim());
    }

    return request<MetricNameInfo[]>(`/api/v1/metrics/names?${params.toString()}`, { signal });
  },

  /** One metric charted, as one line or split by an attribute. */
  metricChart: (
    query: { name: string; show: string | null; split: string | null; range: MetricRange },
    signal?: AbortSignal,
  ) => {
    const params = new URLSearchParams({ name: query.name, range: query.range });

    if (query.show) {
      params.set("show", query.show);
    }

    if (query.split) {
      params.set("split", query.split);
    }

    return request<MetricChart>(`/api/v1/metrics/chart?${params.toString()}`, { signal });
  },

  /** One alert with its timeline and the measure of its rule. */
  alert: (id: string, signal?: AbortSignal) =>
    request<AlertDetails>(`/api/v1/alerts/${encodeURIComponent(id)}`, { signal }),

  /** One page of the alerts of the last days, newest first. */
  alerts: (query: AlertQuery, cursor: string | null, signal?: AbortSignal) => {
    const params = new URLSearchParams({ days: String(query.days) });

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

    if (cursor) {
      params.set("cursor", cursor);
    }

    return request<AlertList>(`/api/v1/alerts?${params.toString()}`, { signal });
  },

  /** The counts of the alerts of the last days, of one environment or of all when it is null. */
  alertSummary: (days: number, environment: string | null, signal?: AbortSignal) =>
    request<AlertSummary>(
      `/api/v1/alerts/summary?days=${days}${environment ? `&environment=${encodeURIComponent(environment)}` : ""}`,
      { signal },
    ),

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

  /** Everyone, with what their badges show. */
  listUsers: (signal?: AbortSignal) => request<UserSummary[]>("/api/v1/users", { signal }),

  /** One person in full. */
  user: (id: string, signal?: AbortSignal) =>
    request<UserDetail>(`/api/v1/users/${encodeURIComponent(id)}`, { signal }),

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

  /** Sends a test message to a channel as entered; with an ID, a webhook without a URL uses the stored one. */
  testDraft: (input: ChannelInput, id: string | null) =>
    request<SendResult>(
      id ? `/api/v1/notification-channels/test?id=${encodeURIComponent(id)}` : "/api/v1/notification-channels/test",
      { method: "POST", body: input },
    ),
};
