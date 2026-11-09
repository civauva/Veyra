/**
 * Types of the Veyra REST API, as described in docs/api/rest.md. Field names match the JSON the API sends.
 */

/** A log severity, from the least to the most severe. */
export type Severity = "Unspecified" | "Trace" | "Debug" | "Information" | "Warning" | "Error" | "Fatal";

/** The severities a person can choose as a minimum, in order. */
export const selectableSeverities: readonly Severity[] = ["Debug", "Information", "Warning", "Error"];

/** A role from ADR 0008. Each includes everything the one before it can do. */
export type Role = "Viewer" | "Editor" | "Admin";

/** The signed-in user. */
export interface Session {
  /** The user's ID. */
  id: string;
  /** The email address the user signs in with. */
  email: string;
  /** The name shown in the UI. */
  displayName: string;
  /** What the user may do. */
  role: Role;
}

/** One stored log record. */
export interface LogEntry {
  /** The storage ID; only orders records with equal timestamps. */
  id: number;
  /** When the event happened, in ISO 8601 UTC. */
  timestamp: string;
  /** When the sender observed it. */
  observedTimestamp: string;
  /** The coarse severity. */
  severity: Severity;
  /** The severity as the sender wrote it. */
  severityText: string | null;
  /** The message. */
  body: string;
  /** The service that wrote the record. */
  serviceName: string;
  /** The environment, for example "production". */
  environment: string | null;
  /** The trace the record belongs to, in lower-case hex. */
  traceId: string | null;
  /** The span the record belongs to, in lower-case hex. */
  spanId: string | null;
  /** The record's own attributes. */
  attributes: Record<string, string>;
  /** The attributes of the resource that wrote it. */
  resourceAttributes: Record<string, string>;
}

/** One page of log records. */
export interface LogsPage {
  /** The records, newest first. */
  items: LogEntry[];
  /** Pass it as `cursor` for the next page; null on the last page. */
  nextCursor: string | null;
}

/** One bar of the log histogram. */
export interface HistogramBucket {
  /** The start of the bucket, in ISO 8601 UTC. */
  start: string;
  /** All matching records in the bucket. */
  total: number;
  /** Matching records at Error or worse. */
  errors: number;
}

/** How many records a service wrote in the window. */
export interface ServiceCount {
  /** The service name. */
  name: string;
  /** The number of matching records. */
  count: number;
}

/** Record counts over the time window, from `GET /api/v1/logs/summary`. */
export interface LogSummary {
  /** The bucket width in seconds. */
  bucketSeconds: number;
  /** The buckets, oldest first, one per interval even when empty. */
  buckets: HistogramBucket[];
  /** The busiest services, most first. Ignores the service filter. */
  services: ServiceCount[];
}

/** An ingestion key, without its secret. */
export interface IngestionKey {
  /** The key's ID. */
  id: string;
  /** What the key is for. */
  name: string;
  /** The start of the key, safe to show. */
  displayPrefix: string;
  /** The environments the key may send data for; null means all. */
  environments: string[] | null;
  /** When the key was created. */
  createdAt: string;
  /** When the key stops working by itself. */
  expiresAt: string | null;
  /** When the key was revoked. */
  revokedAt: string | null;
  /** When a sender last used it. */
  lastUsedAt: string | null;
}

/** A newly created ingestion key, with the secret that is shown only once. */
export interface CreatedIngestionKey {
  /** The key's ID. */
  id: string;
  /** What the key is for. */
  name: string;
  /** The start of the key. */
  displayPrefix: string;
  /** The full key to configure in the sender. */
  secret: string;
}
