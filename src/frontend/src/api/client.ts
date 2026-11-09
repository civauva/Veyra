import type { CreatedIngestionKey, IngestionKey, LogsPage, LogSummary, Session } from "./types";

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

  /** Signs in with a local account. */
  async signIn(email: string, password: string): Promise<Session> {
    const session = await request<Session>("/api/v1/session", { method: "POST", body: { email, password } });
    await refreshAntiforgery();

    return session;
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

  /** All ingestion keys, newest first, without secrets. */
  listKeys: () => request<IngestionKey[]>("/api/v1/ingestion-keys"),

  /** Creates an ingestion key; the answer holds its secret once. */
  createKey: (key: { name: string; environments: string[] | null; expiresAt: string | null }) =>
    request<CreatedIngestionKey>("/api/v1/ingestion-keys", { method: "POST", body: key }),

  /** Revokes an ingestion key. */
  revokeKey: (id: string) =>
    request<undefined>(`/api/v1/ingestion-keys/${encodeURIComponent(id)}`, { method: "DELETE" }),
};
