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
  /** The IANA time zone the user picked on the Account page, or null to use the browser's (VEY-22). */
  timeZone: string | null;
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

/** How serious an open alert is. Red is warning, amber is caution, on every screen (ADR 0007). */
export type AlertSeverity = "Caution" | "Warning";

/**
 * The built-in alert rules (ADR 0012, ADR 0018) and the unit of their measurements: `error-rate` a ratio of records
 * from 0 to 1, `latency-p95` seconds, `disk-space` a share of a disk from 0 to 1, `disk-filling` days until a disk
 * is full, `container-restarts` restarts in 15 minutes, `container-memory` a share of a container's memory limit
 * from 0 to 1, `db-blocking` sessions, `db-deadlocks` deadlocks, `db-long-query` and `db-backup-age` seconds,
 * `db-agent-job` 1 for a failed run, `db-connections` a share of the server's maximum connections from 0 to 1, and
 * `db-wraparound` a database's transaction ID age of the wraparound limit from 0 to 1. The catalog rules (ADR 0020)
 * word themselves from {@link CatalogRule}.
 */
export type AlertRule =
  | "error-rate"
  | "latency-p95"
  | "disk-space"
  | "disk-filling"
  | "container-restarts"
  | "container-memory"
  | "db-blocking"
  | "db-deadlocks"
  | "db-long-query"
  | "db-backup-age"
  | "db-agent-job"
  | "db-connections"
  | "db-wraparound"
  | "db-head-blocker"
  | "db-open-transaction"
  | "db-tempdb-session"
  | "db-plan-regression"
  | "db-offline"
  | "db-log-full"
  | "db-vlf-count"
  | "db-volume-space"
  | "db-log-backup-age"
  | "db-log-chain"
  | "db-checkdb-age"
  | "db-damaged-pages"
  | "db-cpu"
  | "db-cpu-other"
  | "db-workers"
  | "db-grants-pending"
  | "db-os-memory-low"
  | "db-memory-dump"
  | "db-agent-stopped"
  | "db-restart"
  | "db-unsupported"
  | "db-job-overrun"
  | "db-job-late"
  | "db-severe-error"
  | "db-memory-error"
  | "db-login-failures"
  | "db-ag-unhealthy"
  | "db-ag-queue"
  | "db-ag-sync"
  | "db-ag-failover"
  | "db-logshipping";

/** How a catalog rule's values are shown (ADR 0020). */
export type CatalogUnit = "seconds" | "ratio" | "count" | "bytes" | "days" | "flag";

/**
 * A catalog rule as the screens word its alerts (ADR 0020): one entry instead of words written per rule, from
 * `GET /api/v1/alerts/catalog`.
 */
export interface CatalogRule {
  /** The rule's name as alerts store it, such as `db-head-blocker`. */
  id: AlertRule;
  /** Its name on screens, such as "Head blocker". */
  name: string;
  /** What it watches. */
  description: string;
  /** How its values are shown. */
  unit: CatalogUnit;
  /** For a count, what is counted, in the singular. */
  noun: string | null;
  /** What is wrong, with `{server}`, `{part}`, and `{value}` to fill in. */
  headline: string;
  /** Where it stands once resolved, with the same placeholders. */
  recovery: string;
  /** What a part of the server is for this rule, such as "Database", or null. */
  partLabel: string | null;
  /** Whether a lower value is worse. */
  lowerIsWorse: boolean;
  /** Whether it runs. */
  enabled: boolean;
  /** The caution level in its unit, or null for none. */
  caution: number | null;
  /** The warning level in its unit. */
  warning: number;
}

/**
 * What a block is (ADR 0018): a service seen in its logs or metrics, a host, a database server named by its engine
 * and server such as `microsoft.sql_server sql-prod-01`, or a container.
 */
export type BlockKind = "service" | "host" | "database" | "container";

/** What an alert is about (ADR 0012). */
export interface AlertBlock {
  /** A service or a host. */
  kind: BlockKind;
  /** The service or host name. */
  name: string;
  /** The part of the block, for example a disk's mount point; null for none. */
  part: string | null;
  /** The block's identity on the home, for example `host:production/web-01`. */
  id: string;
}

/** The weather of an environment: clear without alerts, showers with cautions only, storm with any warning. */
export type ConditionKind = "Clear" | "Showers" | "Storm";

/** The state of one block, set only by alerts. */
export type BlockHealth = "Ok" | "Caution" | "Warning" | "Unknown";

/** One environment pill. */
export interface HomeEnvironment {
  /** The environment name. */
  name: string;
  /** Its weather. */
  condition: ConditionKind;
  /** The share of its records that are errors right now, from 0 to 1; null without records. */
  errorRatio: number | null;
}

/** One alert in the Needs you pane, or one resolved recently. */
export interface HomeAlert {
  /** Its number in the Needs you pane, from 1; null when resolved. */
  n: number | null;
  /** The alert ID. */
  id: string;
  /** The block it is about, for example `service:production/payments-gateway` or `host:production/web-01`. */
  block: string;
  /** The service name, for service alerts; null for host alerts. */
  service: string | null;
  /** What it is about: a service or a host (ADR 0012). */
  kind: BlockKind;
  /** The service or host name. */
  name: string;
  /** The part of the block, for example a disk's mount point; null for none. */
  part: string | null;
  /** The environment; null when the sender set none. */
  environment: string | null;
  /** The rule that raised it. */
  rule: AlertRule;
  /** The current, or last, severity. */
  severity: AlertSeverity;
  /** The latest measurement, in the rule's unit (see {@link AlertRule}). */
  value: number;
  /** The worst measurement while it was open. */
  peak: number;
  /** The threshold of its severity. */
  threshold: number;
  /** The window the rule measures over, in seconds. */
  windowSeconds: number;
  /** When it opened. */
  since: string;
  /** When it resolved; null while open. */
  resolvedAt: string | null;
}

/** One monitored thing: a service seen in its logs, or a host with an open alert. */
export interface HomeBlock {
  /** Stable identity. */
  id: string;
  /** What it is. */
  kind: BlockKind;
  /** The display name. */
  name: string;
  /** Its environment. */
  environment: string | null;
  /** Its state, from its open alerts only. */
  health: BlockHealth;
  /** The alerts behind the state. */
  alertIds: string[];
  /** Its records in the last five minutes; 0 for hosts. */
  records: number;
  /** Its error records in the last five minutes; 0 for hosts. */
  errors: number;
  /** The share of errors; null without records. */
  errorRatio: number | null;
}

/** The expected load of a coming hour. */
export interface ExpectedHour {
  /** The start of the hour. */
  start: string;
  /** Its usual record count relative to an average hour; null while still learning. */
  load: number | null;
}

/** Log storage and when it reaches its budget. */
export interface HomeStorage {
  /** All stored logs, indexes included. */
  usedBytes: number;
  /** The configured budget; null when none is set. */
  budgetBytes: number | null;
  /** The average size of a full day; null before the first full day. */
  dailyBytes: number | null;
  /** How many whole days are kept before today. */
  retentionDays: number;
  /** Days until the budget is reached; null when it is not reached or not known. */
  daysUntilBudget: number | null;
  /** Whether the budget is reached before retention removes old days; null when not known. */
  reachesBudget: boolean | null;
}

/** The home snapshot from `GET /api/v1/home` (docs/api/rest.md). */
export interface HomeSnapshot {
  /** The environment shown; null for all. */
  environment: string | null;
  /** When the snapshot was taken. */
  at: string;
  /** Every environment seen recently, by name. */
  environments: HomeEnvironment[];
  /** The weather, where it is, and the error ratio behind it. */
  condition: { kind: ConditionKind; over: string | null; errorRatio: number | null };
  /** Blocks per state, for the master lights. */
  counts: { warning: number; caution: number; ok: number; unknown: number };
  /** Open alerts, numbered: warnings first, then oldest first. */
  alerts: HomeAlert[];
  /** Alerts resolved in the last 24 hours, newest first. */
  resolved: HomeAlert[];
  /** Every service seen in the last hour or with an open alert, by name, then every host with an open alert. */
  blocks: HomeBlock[];
  /** The key metrics over the last few minutes. */
  metrics: {
    errorRatio: number | null;
    ingestPerSecond: number;
    records: number;
    windowSeconds: number;
    /** HTTP request durations from metrics; null when no service sends them. */
    latency: HomeLatency | null;
  };
  /** The past hours observed and the coming hours expected. */
  hourly: { observed: HistogramBucket[]; expected: ExpectedHour[]; daysOfHistory: number };
  /** Log storage and its outlook. */
  storage: HomeStorage;
  /** Every disk watched through host metrics: those that fill soonest first, then the fullest. */
  disks: HomeDisk[];
}

/** How fast the environment answers HTTP requests over the last five minutes. */
export interface HomeLatency {
  /** The 95th percentile of every service together, in seconds. */
  p95Seconds: number;
  /** The service with the highest 95th percentile. */
  slowestService: string;
  /** Its 95th percentile, in seconds. */
  slowestP95Seconds: number;
  /** How many services sent request durations. */
  services: number;
}

/** A disk watched through host metrics. */
export interface HomeDisk {
  /** The host. */
  host: string;
  /** Where the filesystem is mounted. */
  mountpoint: string;
  /** The share in use, from 0 to 1. */
  ratio: number;
  /** When it is full at the pace of the last day, in days; null when it is not filling. */
  daysUntilFull: number | null;
}

/** What changed about an alert. */
export type AlertEventKind = "Opened" | "Escalated" | "Eased" | "Resolved";

/** How a notification channel delivers. */
export type ChannelKind = "Webhook" | "Email";

/** Where one message to one channel stands. */
export type DeliveryState = "Pending" | "Sent" | "Failed";

/** One message about an alert event to one channel. */
export interface AlertDelivery {
  /** The channel's ID. */
  channelId: string;
  /** The channel's name. */
  channelName: string;
  /** Webhook or email. */
  channelKind: ChannelKind;
  /** Pending, sent, or failed for good. */
  state: DeliveryState;
  /** How many tries were made. */
  attempts: number;
  /** When the last try ended. */
  lastAttemptAt: string | null;
  /** The HTTP status of the last webhook try. */
  lastStatus: number | null;
  /** Why the last try failed. */
  lastError: string | null;
  /** When the next try is due, while pending. */
  nextAttemptAt: string | null;
  /** When the channel accepted it. */
  sentAt: string | null;
}

/** One change of an alert, with the messages it caused. */
export interface AlertEvent {
  /** What changed. */
  kind: AlertEventKind;
  /** The severity after the change; for resolved, the last one. */
  severity: AlertSeverity;
  /** The measurement that caused it, in the rule's unit. */
  value: number;
  /** When it happened. */
  at: string;
  /** One message per channel. */
  deliveries: AlertDelivery[];
}

/** One point of the alert's error-rate chart. */
export interface AlertPoint {
  /** The end of the bucket. */
  at: string;
  /** Records in the bucket. */
  total: number;
  /** Error records in the bucket. */
  errors: number;
  /** Records in the rule's window ending here. */
  windowTotal: number;
  /** Error records in that window. */
  windowErrors: number;
  /** The error ratio the rule saw here; null without records. */
  ratio: number | null;
}

/** The most frequent error messages of the alert. */
export interface MessageGroup {
  /** The message. */
  message: string;
  /** How many records have it. */
  count: number;
  /** The oldest of them. */
  first: string;
  /** The newest of them. */
  last: string;
}

/** The disk of a disk alert, with its week and the host's other disks. */
export interface AlertDisk {
  /** The disk's mount point. */
  mountpoint: string;
  /** The share in use now; null when the host no longer reports it. */
  ratio: number | null;
  /** The bytes in use now. */
  usedBytes: number | null;
  /** The disk's size. */
  totalBytes: number | null;
  /** When it is full at the pace of the last day; null when not filling. */
  daysUntilFull: number | null;
  /** The width of each point. */
  bucketSeconds: number;
  /** The start of the chart: a week before the alert opened. */
  from: string;
  /** The end of the chart. */
  to: string;
  /** The share in use per point; null where nothing was measured. */
  points: { at: string; ratio: number | null }[];
  /** Every disk of the host now, fullest first. */
  disks: { mountpoint: string; ratio: number; daysUntilFull: number | null }[];
}

/** One route of a latency alert's service while it was open. */
export interface AlertRoute {
  /** The HTTP method, when sent. */
  method: string | null;
  /** The route template. */
  route: string;
  /** Requests per second. */
  requestsPerSecond: number;
  /** The 95th percentile, in seconds. */
  p95Seconds: number | null;
  /** The share of 5xx answers. */
  serverErrorRatio: number | null;
}

/** The request durations of a latency alert's service around the alert. */
export interface AlertLatency {
  /** The width of each point. */
  bucketSeconds: number;
  /** The start of the first point. */
  from: string;
  /** The end of the last point. */
  to: string;
  /** The median, the 95th percentile, and the requests per point. */
  points: { at: string; p50Seconds: number | null; p95Seconds: number | null; requests: number }[];
  /** Its routes while the alert was open, slowest first. */
  routes: AlertRoute[];
}

/** One alert from `GET /api/v1/alerts/{id}` (docs/api/rest.md). */
export interface AlertDetails {
  /** The alert ID. */
  id: string;
  /** The rule that raised it. */
  rule: AlertRule;
  /** The environment; null when the sender set none. */
  environment: string | null;
  /** What it is about. */
  block: AlertBlock;
  /** The service, for service alerts; null for host alerts. */
  service: string | null;
  /** The severity now, or the last one. */
  severity: AlertSeverity;
  /** The highest severity it ever had. */
  worst: AlertSeverity;
  /** The latest measurement, in the rule's unit. */
  value: number;
  /** The worst measurement: the highest, or for days until full the lowest. */
  peak: number;
  /** When the peak was measured; null for alerts from before Veyra recorded it. */
  peakAt: string | null;
  /** The threshold of the current severity. */
  threshold: number;
  /** The rule's caution threshold. */
  cautionThreshold: number;
  /** The rule's warning threshold. */
  warningThreshold: number;
  /** Where a caution of the rule ends. */
  clearThreshold: number;
  /** Whether a lower measurement is worse, as for days until full. */
  lowerIsWorse: boolean;
  /** The rule's window in seconds. */
  windowSeconds: number;
  /** For the error-rate rule, the fewest records before it judges a service; otherwise null. */
  minimumRecords: number | null;
  /** For the latency rule, the fewest requests before it judges a service; otherwise null. */
  minimumRequests: number | null;
  /** When it opened. */
  openedAt: string;
  /** When it was last measured. */
  updatedAt: string;
  /** When it resolved; null while open. */
  resolvedAt: string | null;
  /** What happened, oldest first. */
  events: AlertEvent[];
  /** For the error-rate rule, the error rate around the alert; otherwise null. */
  series: { bucketSeconds: number; from: string; to: string; points: AlertPoint[] } | null;
  /** For the error-rate rule, the most frequent error messages; otherwise empty. */
  topErrors: MessageGroup[];
  /** For the disk rules, the disk; otherwise null. */
  disk: AlertDisk | null;
  /** For the latency rule, the request durations; otherwise null. */
  latency: AlertLatency | null;
}

/** One alert in the list of `GET /api/v1/alerts`. */
export interface AlertListItem {
  /** The alert ID. */
  id: string;
  /** The rule that raised it. */
  rule: AlertRule;
  /** The environment; null when the sender set none. */
  environment: string | null;
  /** What it is about. */
  block: AlertBlock;
  /** The severity now, or the last one. */
  severity: AlertSeverity;
  /** The highest severity it ever had. */
  worst: AlertSeverity;
  /** The latest measurement. */
  value: number;
  /** The worst measurement. */
  peak: number;
  /** When the peak was measured, if known. */
  peakAt: string | null;
  /** The threshold of the current severity. */
  threshold: number;
  /** The rule's window in seconds. */
  windowSeconds: number;
  /** When it opened. */
  openedAt: string;
  /** When it was last measured. */
  updatedAt: string;
  /** When it resolved; null while open. */
  resolvedAt: string | null;
}

/** One page of `GET /api/v1/alerts`, newest first. */
export interface AlertList {
  /** The alerts. */
  items: AlertListItem[];
  /** The cursor of the next page; null on the last. */
  next: string | null;
}

/** The counts of `GET /api/v1/alerts/summary` over the last days. */
export interface AlertSummary {
  /** The start of the period: midnight in the zone the request named, UTC when it named none. */
  from: string;
  /** Now. */
  to: string;
  /** Alerts open now. */
  open: number;
  /** Open alerts at warning. */
  openWarnings: number;
  /** Every alert open at some time in the period. */
  total: number;
  /** Those that were at warning at some time. */
  warnings: number;
  /** Those that resolved. */
  resolved: number;
  /** The median time from open to resolved, in seconds; null when none resolved. */
  medianSeconds: number | null;
  /** Alerts per built-in rule, every rule listed. */
  rules: {
    rule: AlertRule;
    count: number;
    enabled: boolean;
    cautionThreshold: number;
    warningThreshold: number;
    windowSeconds: number;
  }[];
  /** One entry per day in the zone the request named, oldest first. */
  days: { date: string; count: number; worst: AlertSeverity | null }[];
}

/** The filters of the Alerts screen, as `GET /api/v1/alerts` takes them. */
export interface AlertQuery {
  /** How many days back. */
  days: number;
  /** Open, resolved, or all alerts. */
  state: "open" | "resolved" | "all";
  /** Only these rules; empty for all. */
  rules: AlertRule[];
  /** Only alerts whose service, host, or disk holds this text. */
  text: string;
  /** Only alerts of this environment, the one chosen in the top bar; null for all. */
  environment: string | null;
}

/** A channel's deliveries on one UTC day. */
export interface ChannelDay {
  /** The day, `YYYY-MM-DD`. */
  day: string;
  /** Messages the channel accepted. */
  sent: number;
  /** Messages whose last try failed. */
  failed: number;
}

/** A notification channel from `GET /api/v1/notification-channels`. */
export interface NotificationChannel {
  /** The channel ID. */
  id: string;
  /** Its name. */
  name: string;
  /** Webhook or email. */
  kind: ChannelKind;
  /** Whether it receives anything. */
  enabled: boolean;
  /** Whether it hears of warnings. */
  sendWarning: boolean;
  /** Whether it hears of cautions. */
  sendCaution: boolean;
  /** Whether it hears that alerts it was told about resolved. */
  sendResolved: boolean;
  /** The start of the webhook URL; the URL itself is never sent back. */
  webhookUrlHint: string | null;
  /** The email addresses. */
  emailRecipients: string[] | null;
  /** When it was added. */
  createdAt: string;
  /** When it was last changed. */
  updatedAt: string;
  /** What it did over the last 14 days. */
  activity: {
    days: ChannelDay[];
    sent: number;
    failed: number;
    lastAttemptAt: string | null;
    lastError: string | null;
    lastStatus: number | null;
    nextAttemptAt: string | null;
  };
}

/** The SMTP server email channels use. It is set in the environment, not on the screen. */
export interface EmailServer {
  /** Whether a server is set. */
  configured: boolean;
  /** Its host name. */
  host: string | null;
  /** Its port. */
  port: number;
  /** How the connection is secured. */
  security: "Auto" | "StartTls" | "SslOnConnect" | "None";
  /** The sender address. */
  from: string | null;
}

/** A channel as entered, to create, change, or test it. */
export interface ChannelInput {
  /** The name, unique ignoring case. */
  name: string;
  /** Webhook or email. */
  kind: ChannelKind;
  /** Whether it receives anything. */
  enabled: boolean;
  /** Whether it hears of warnings. */
  sendWarning: boolean;
  /** Whether it hears of cautions. */
  sendCaution: boolean;
  /** Whether it hears of resolutions. */
  sendResolved: boolean;
  /** The webhook URL; null keeps the stored one when changing a webhook. */
  webhookUrl: string | null;
  /** The email addresses. */
  emailRecipients: string[] | null;
}

/** How a test message went. */
export interface SendResult {
  /** Whether the channel accepted it. */
  succeeded: boolean;
  /** The HTTP status of a webhook's answer. */
  status: number | null;
  /** Why it failed. */
  error: string | null;
}

/** One point of a chart line. */
export interface ChartPoint {
  /** The start of the bucket. */
  at: string;
  /** The value; null when there was nothing to compute it from. */
  value: number | null;
}

/** One line of a chart: one value per bucket, oldest first. */
export interface ChartLine {
  /** What the line shows, for example `user` or `p95`. */
  label: string;
  /** The last value that is not null. */
  latest: number | null;
  /** The points. */
  points: ChartPoint[];
}

/** One mounted filesystem of a host. */
export interface MetricDisk {
  /** Where it is mounted. */
  mountpoint: string;
  /** The device. */
  device: string | null;
  /** Space in use, in bytes. */
  usedBytes: number;
  /** Space in use plus free space, in bytes. */
  totalBytes: number;
  /** The share in use, from 0 to 1. */
  ratio: number;
  /** When it is full at the pace of the last day; null when it is not filling. */
  daysUntilFull: number | null;
}

/** A host on the Metrics overview. */
export interface HostCard {
  /** From `host.name`. */
  name: string;
  /** The environment its metrics name, if any. */
  environment: string | null;
  /** The operating system. */
  os: string | null;
  /** How many CPUs report time. */
  cpus: number | null;
  /** Total memory in bytes. */
  memoryBytes: number | null;
  /** When a point last arrived. */
  lastSeen: string;
  /** CPU in use over the last five minutes, from 0 to 1. */
  cpuRatio: number | null;
  /** Memory in use, from 0 to 1. */
  memoryRatio: number | null;
  /** The one-minute load average. */
  load: number | null;
  /** The disk with the highest share in use. */
  fullestDisk: MetricDisk | null;
  /** The disk that is full soonest, within 30 days. */
  fillingDisk: MetricDisk | null;
  /** CPU in use over the window. */
  cpu: ChartLine;
}

/** A service on the Metrics overview. */
export interface ServiceCard {
  /** From `service.name`. */
  name: string;
  /** From `deployment.environment.name`. */
  environment: string | null;
  /** The SDK's language, for example `.NET`. */
  runtime: string | null;
  /** How many instances send metrics. */
  instances: number;
  /** When a point last arrived. */
  lastSeen: string;
  /** Requests per second over the last five minutes. */
  requestsPerSecond: number | null;
  /** Their 95th percentile duration, in seconds. */
  p95Seconds: number | null;
  /** The share answered with a 5xx status. */
  serverErrorRatio: number | null;
  /** CPU in use, from 0 to 1. */
  cpuRatio: number | null;
  /** Memory in bytes. */
  memoryBytes: number | null;
  /** The 95th percentile over the window. */
  latency: ChartLine;
}

/** How much metric data is stored. */
export interface MetricUsage {
  /** Stored series. */
  series: number;
  /** The series limit. */
  maxSeries: number;
  /** Points received per second. */
  pointsPerSecond: number;
  /** Disk space in bytes. */
  usedBytes: number;
  /** Days of points kept. */
  retentionDays: number;
  /** Metric names seen within the window. */
  names: number;
}

/** The range a metrics screen shows. */
export type MetricRange = "1h" | "6h" | "24h" | "7d";

/** The Metrics overview from `GET /api/v1/metrics/overview`. */
export interface MetricsOverview {
  /** When it was put together. */
  at: string;
  /** The window of the lines. */
  range: MetricRange;
  /** The width of each point. */
  bucketSeconds: number;
  /** How much metric data is stored. */
  usage: MetricUsage;
  /** The hosts. */
  hosts: HostCard[];
  /** The services. */
  services: ServiceCard[];
  /** The database servers (ADR 0018). */
  databases: DatabaseCard[];
  /** The Docker containers, by name and host (ADR 0018). */
  containers: ContainerCard[];
}

/**
 * A Docker container on the Metrics overview and on its host's screen, from the Collector's `docker_stats` receiver
 * (ADR 0018).
 */
export interface ContainerCard {
  /** From `container.name`. */
  name: string;
  /** The host it runs on, from `host.name`; null when the Collector did not add it. */
  host: string | null;
  /** The environment its metrics name, if any. */
  environment: string | null;
  /** The image, from `container.image.name`. */
  image: string | null;
  /** The short container ID, as Docker shows it. */
  id: string | null;
  /** When a point last arrived. */
  lastSeen: string;
  /** CPU in use over the last five minutes, in percent of one CPU: above 100 when it uses more than one. */
  cpuPercent: number | null;
  /** Memory in use now, in bytes, without the page cache. */
  memoryBytes: number | null;
  /** Its memory limit in bytes; Docker reports the host's memory for a container without one. */
  memoryLimitBytes: number | null;
  /** Memory in use of its limit, from 0 to 1. */
  memoryRatio: number | null;
  /** Restarts within the range; null when the container sends no restart count. */
  restarts: number | null;
  /** Docker's count of its restarts since it was created. */
  restartsTotal: number | null;
  /** How long it has run since it last started, in seconds. */
  uptimeSeconds: number | null;
  /** CPU in use over the range, in percent of one CPU. */
  cpu: ChartLine;
}

/** A database server on the Metrics overview, and the head of its screen (ADR 0018). */
export interface DatabaseCard {
  /** The engine, as `db.system.name` spells it, such as `microsoft.sql_server`. */
  engine: string;
  /** The server, as `server.address` names it, such as `sql-prod-01`. */
  server: string;
  /** The environment; null when the server sent none. */
  environment: string | null;
  /** When the server last sent a point. */
  lastSeen: string;
  /** The logical CPUs SQL Server sees. */
  cpus: number | null;
  /** The memory SQL Server uses, in bytes. */
  memoryBytes: number | null;
  /** How long the machine has been up, in seconds. */
  uptimeSeconds: number | null;
  /** Batch requests per second now. */
  batchRequests: number | null;
  /** User connections now. */
  connections: number | null;
  /** Sessions blocked now. */
  blocked: number | null;
  /** Deadlocks in the last 15 minutes. */
  deadlocks: number | null;
  /** Page life expectancy now, in seconds. */
  pageLifeSeconds: number | null;
  /** The longest running query now, of any database, in seconds. */
  longestQuerySeconds: number | null;
  /** How many databases the server has. */
  databases: number;
  /** The longest any database has gone without a full backup, in seconds. */
  oldestBackupSeconds: number | null;
  /** SQL Agent jobs whose last run failed. */
  failedJobs: number;
  /** Batch requests per second over the window; for PostgreSQL, transactions committed or rolled back per second. */
  requests: ChartLine;
  /** The server's maximum connections; null where the engine does not say it, as only PostgreSQL does. */
  maxConnections: number | null;
  /** Committed transactions per second now, of every database (PostgreSQL). */
  commitsPerSecond: number | null;
  /** Rolled back transactions per second now, of every database (PostgreSQL). */
  rollbacksPerSecond: number | null;
  /** The highest transaction ID age of any database, of the wraparound limit, from 0 to 1 (PostgreSQL). */
  wraparoundRatio: number | null;
}

/** One database of a server. */
export interface DatabasePart {
  /** The database name. */
  name: string;
  /** Its data and log files together, in bytes. */
  sizeBytes: number | null;
  /** Its log files, in bytes. */
  logBytes: number | null;
  /** How much it grew over the last day, in bytes; null without a day of points. */
  growthBytesPerDay: number | null;
  /** Seconds since its last full backup; null when it has never had one. */
  backupAgeSeconds: number | null;
  /** The average time of a file read or write over the window, in seconds. */
  ioLatencySeconds: number | null;
  /** The longest query running in it now, in seconds. */
  longestQuerySeconds: number | null;
  /** Connections to it now (PostgreSQL). */
  connections: number | null;
  /** The age of its oldest unfrozen transaction ID, in transactions (PostgreSQL). */
  transactionIdAge: number | null;
  /** That age of the wraparound limit of 2^31, from 0 to 1 (PostgreSQL). */
  wraparoundRatio: number | null;
  /** Live rows of all its tables (PostgreSQL). */
  liveRows: number | null;
  /** Dead rows of all its tables, left behind until a vacuum clears them (PostgreSQL). */
  deadRows: number | null;
  /** Dead rows of all its rows, from 0 to 1 (PostgreSQL). */
  deadRowsRatio: number | null;
  /** How many times its tables were vacuumed over the last day (PostgreSQL). */
  vacuums: number | null;
}

/** A PostgreSQL table with dead rows, which updates and deletes leave behind until a vacuum clears them. */
export interface DatabaseTable {
  /** The database it is in. */
  database: string;
  /** Its schema. */
  schema: string | null;
  /** The table name. */
  name: string;
  /** Its live rows now. */
  liveRows: number | null;
  /** Its dead rows now. */
  deadRows: number | null;
  /** Dead rows of all its rows, from 0 to 1. */
  deadRowsRatio: number | null;
  /** How many times it was vacuumed over the last day. */
  vacuums: number | null;
}

/** The last run of a SQL Agent job. */
export interface AgentJob {
  /** The job name. */
  name: string;
  /** Whether the last run failed. */
  failed: boolean;
  /** When the last run started; null when not sent. */
  lastRun: string | null;
  /** How long the last run took, in seconds. */
  durationSeconds: number | null;
}

/** A request running now for longer than ten seconds. */
export interface RunningQuery {
  /** The session ID. */
  session: string | null;
  /** The database it runs in. */
  database: string | null;
  /** The login that runs it. */
  user: string | null;
  /** How long it has been running, in seconds. */
  elapsedSeconds: number;
  /** Running, runnable, or suspended. */
  status: string | null;
  /** What it waits on now, such as `LCK_M_U`; null when it does not wait. */
  wait: string | null;
  /** The session that blocks it; null when nothing does. */
  blockedBy: string | null;
  /** The statement. */
  text: string;
}

/** One of the queries with the most total time in the server's latest report. */
export interface TopQuery {
  /** The statement. */
  text: string;
  /** Its total elapsed time, in seconds. */
  totalSeconds: number;
  /** How many times it ran. */
  executions: number;
  /** Its CPU time, in seconds. */
  workerSeconds: number | null;
  /** Its pages read from memory. */
  logicalReads: number | null;
  /** The database it ran in, when the agent said. */
  database: string | null;
}

/** What Veyra's agent itself asked of a server over the range, kept out of the top queries. */
export interface AgentWork {
  /** How many different statements of the agent ran. */
  statements: number;
  /** Their total elapsed time, in seconds. */
  totalSeconds: number;
}

/** One database server's screen from `GET /api/v1/metrics/databases/{engine}/{server}` (ADR 0018). */
export interface DatabaseMetrics {
  /** The head of the screen. */
  card: DatabaseCard;
  /** The window. */
  range: MetricRange;
  /** The width of a point of the lines, in seconds. */
  bucketSeconds: number;
  /** Batch requests per second; for PostgreSQL, transactions committed or rolled back per second. */
  requests: ChartLine;
  /** User connections; for PostgreSQL, the connections to every database. */
  connections: ChartLine;
  /** Sessions blocked. */
  blocked: ChartLine;
  /** Deadlocks per point. */
  deadlocks: ChartLine;
  /** Page life expectancy in seconds. */
  pageLife: ChartLine;
  /** The longest running query in seconds, of any database. */
  longestQuery: ChartLine;
  /** Seconds waited per second, one line per wait category, the largest first. */
  waits: ChartLine[];
  /** The server's databases, the largest first. */
  databases: DatabasePart[];
  /** SQL Agent jobs, the failed ones first. */
  jobs: AgentJob[];
  /** Requests running for longer than ten seconds now, the longest first. */
  running: RunningQuery[];
  /** The statements with the most total time over the range, the agent's own left out. */
  topQueries: TopQuery[];
  /** What Veyra's agent itself asked of the server over the range; null when nothing. */
  agentWork: AgentWork | null;
  /** The server's maximum connections over the range; null where the engine does not say it. */
  maxConnections: ChartLine | null;
  /** Committed transactions per second of every database (PostgreSQL); null for other engines. */
  commits: ChartLine | null;
  /** Rolled back transactions per second of every database (PostgreSQL); null for other engines. */
  rollbacks: ChartLine | null;
  /** The tables with the most dead rows now, the most first (PostgreSQL); empty for other engines. */
  tables: DatabaseTable[];
}

/** One host's metrics from `GET /api/v1/metrics/hosts/{host}`. */
export interface HostMetrics {
  /** The card numbers. */
  card: HostCard;
  /** The window. */
  range: MetricRange;
  /** The width of each point. */
  bucketSeconds: number;
  /** CPU in use: total, user, system, wait. */
  cpu: ChartLine[];
  /** Memory: used, cache. */
  memory: ChartLine[];
  /** Every filesystem, fullest first. */
  disks: MetricDisk[];
  /** Bytes per second: received, sent. */
  network: ChartLine[];
  /** Bytes per second: read, written. */
  diskIo: ChartLine[];
  /** Load averages: 1m, 5m, 15m. */
  load: ChartLine[];
  /** The Docker containers on the host that sent within the range, by name (ADR 0018). */
  containers: ContainerCard[];
}

/** One HTTP route of a service over the last five minutes. */
export interface MetricRoute {
  /** The HTTP method. */
  method: string | null;
  /** The route template. */
  route: string;
  /** Requests per second. */
  requestsPerSecond: number;
  /** The 95th percentile duration, in seconds. */
  p95Seconds: number | null;
  /** The share answered with a 5xx status. */
  serverErrorRatio: number | null;
}

/** One service's metrics from `GET /api/v1/metrics/services/{service}`. */
export interface ServiceMetrics {
  /** The card numbers. */
  card: ServiceCard;
  /** The window. */
  range: MetricRange;
  /** The width of each point. */
  bucketSeconds: number;
  /** The instances. */
  instanceNames: string[];
  /** Request duration in seconds: p50, p95, p99. */
  latency: ChartLine[];
  /** Requests per second: ok, 4xx, 5xx. */
  requests: ChartLine[];
  /** The busiest routes. */
  routes: MetricRoute[];
  /** CPU from 0 to 1, and memory in bytes. */
  resources: ChartLine[];
  /** Runtime: gc per minute, queue, threads. */
  runtime: ChartLine[];
}

/** The kinds of metric Veyra stores. */
export type MetricKind = "Gauge" | "Sum" | "Histogram";

/** One metric name from `GET /api/v1/metrics/names`. */
export interface MetricNameInfo {
  /** The metric name. */
  name: string;
  /** Gauge, sum, or histogram. */
  kind: MetricKind;
  /** The unit as sent. */
  unit: string;
  /** The description as sent. */
  description: string;
  /** How many series have the name. */
  series: number;
  /** The services or hosts that send it. */
  sources: string[];
}

/** One metric charted, from `GET /api/v1/metrics/chart`. */
export interface MetricChart {
  /** The metric name. */
  name: string;
  /** Gauge, sum, or histogram. */
  kind: MetricKind;
  /** The unit of the lines. */
  unit: string;
  /** What the lines show. */
  show: "value" | "rate" | "p50" | "p95" | "p99";
  /** The attribute the lines are split by. */
  split: string | null;
  /** The attributes it can be split by. */
  splitKeys: string[];
  /** The width of each point. */
  bucketSeconds: number;
  /** At most eight lines. */
  lines: ChartLine[];
}

/** What follows a correct password when the session does not start yet (ADR 0011). */
export type SignInNext = "code" | "setup";

/** The answer of the password step: a session, or the step that comes next. */
export type PasswordStep = { session: Session; next?: undefined } | { session?: undefined; next: SignInNext };

/** What a person needs to add Veyra to an authenticator app. Shown once. */
export interface SecondFactorSetup {
  /** The secret in Base32, for typing it in by hand. */
  secret: string;
  /** The otpauth address the QR code shows. */
  uri: string;
}

/** A switched-on second factor. */
export interface SecondFactorInfo {
  /** When it was switched on. */
  since: string;
  /** Recovery codes not used yet. */
  recoveryCodesLeft: number;
}

/** How a session was signed in. */
export type SessionMethod = "Password" | "Code" | "RecoveryCode" | "Provider";

/** One signed-in browser. */
export interface SessionInfo {
  /** The session ID, for ending it. */
  id: string;
  /** The browser and system, for example "Firefox on Ubuntu". */
  browser: string;
  /** The address it was signed in from. */
  address: string | null;
  /** When it was signed in. */
  startedAt: string;
  /** The last request, to within five minutes. */
  lastActiveAt: string;
  /** How it was signed in. */
  method: SessionMethod;
  /** Whether it is the session making the request. */
  current: boolean;
}

/** How a sign-in attempt ended. */
export type SignInOutcome =
  | "SignedIn"
  | "SignedInWithCode"
  | "SignedInWithRecoveryCode"
  | "WrongPassword"
  | "WrongCode"
  | "Locked"
  | "SwitchedOff";

/** One sign-in attempt. */
export interface SignInAttempt {
  /** When it happened. */
  at: string;
  /** How it ended. */
  outcome: SignInOutcome;
  /** The client address. */
  address: string | null;
  /** The browser and system. */
  browser: string;
  /** Whether the account had not signed in from this address before. */
  newAddress: boolean;
}

/** What changed on an account. */
export type UserEventKind =
  | "Added"
  | "RoleChanged"
  | "SwitchedOff"
  | "SwitchedOn"
  | "PasswordChanged"
  | "PasswordReset"
  | "SecondFactorOn"
  | "SecondFactorOff"
  | "SecondFactorRemoved"
  | "RecoveryCodesReplaced"
  | "SessionsEnded"
  | "TeamChanged";

/** One change to an account. */
export interface UserEvent {
  /** When it happened. */
  at: string;
  /** What changed. */
  kind: UserEventKind;
  /** Who changed it, or null when Veyra did. */
  by: string | null;
  /** What it changed to: the old and new role, the team, or a count. */
  detail: string | null;
}

/** A person, as the Users list shows them. */
export interface UserSummary {
  /** The person's ID. */
  id: string;
  /** The email address they sign in with. */
  email: string;
  /** The name shown in the UI. */
  displayName: string;
  /** What they may do. */
  role: Role;
  /** Whether they are switched off. */
  disabled: boolean;
  /** When the account was added. */
  createdAt: string;
  /** When they last signed in, or null if never. */
  lastSignInAt: string | null;
  /** The team an Admin set, or null. */
  team: string | null;
  /** When a lock after wrong attempts ends, or null. */
  lockedUntil: string | null;
  /** When their second factor was switched on, or null. */
  secondFactorSince: string | null;
  /** How many browsers are signed in now. */
  activeSessions: number;
  /** The newest activity of any session, or null. */
  lastActiveAt: string | null;
}

/** One person in full, for the person panel. */
export interface UserDetail {
  /** The account, as the list shows it. */
  user: UserSummary;
  /** The name of the Admin who added the account, or null. */
  addedBy: string | null;
  /** The second factor when it is on, or null. */
  secondFactor: SecondFactorInfo | null;
  /** Whether the policy requires one of this person. */
  secondFactorRequired: boolean;
  /** Active sessions, most recently active first. */
  sessions: SessionInfo[];
  /** The latest sign-in attempts, newest first. */
  signIns: SignInAttempt[];
  /** Successful sign-ins within 30 days. */
  signedInLast30Days: number;
  /** Failed attempts within 30 days. */
  failedLast30Days: number;
  /** Changes to the account, newest first. */
  events: UserEvent[];
}

/** The signed-in person's own account. */
export interface Account {
  /** The person's ID. */
  id: string;
  /** The email address. */
  email: string;
  /** The name shown in the UI. */
  displayName: string;
  /** What the person may do. */
  role: Role;
  /** The team an Admin set, or null. */
  team: string | null;
  /** When the account was added. */
  createdAt: string;
  /** When the person last signed in. */
  lastSignInAt: string | null;
  /** Whether the account has a local password it can change. */
  hasPassword: boolean;
  /** The second factor when it is on, or null. */
  secondFactor: SecondFactorInfo | null;
  /** Whether an Admin requires one of this person. */
  secondFactorRequired: boolean;
  /** The IANA time zone the person picked, or null to use the browser's (VEY-22). */
  timeZone: string | null;
  /** Active sessions, the current one marked. */
  sessions: SessionInfo[];
  /** The latest sign-in attempts, newest first. */
  signIns: SignInAttempt[];
}

/** Who must sign in with a second factor. */
export type SecondFactorPolicy = "Nobody" | "Admins" | "Everyone";

/** A new person, as an Admin adds them. */
export interface NewUser {
  /** The email address they sign in with. */
  email: string;
  /** The name shown in the UI; the email when empty. */
  displayName: string;
  /** What they may do. */
  role: Role;
  /** The first password. */
  password: string;
  /** The team, or null. */
  team: string | null;
}

/** Changes to a person; fields left out stay as they are. */
export interface UserChanges {
  /** The new role. */
  role?: Role;
  /** Whether they are switched off. */
  disabled?: boolean;
  /** The new team; an empty string removes it. */
  team?: string;
}

/** What a span does in its trace (ADR 0014). */
export type SpanKind = "Unspecified" | "Internal" | "Server" | "Client" | "Producer" | "Consumer";

/** How a span ended; unset counts as success. */
export type SpanStatus = "Unset" | "Ok" | "Error";

/** Why a trace failed, from its deepest failed span. */
export interface TraceCause {
  /** The service of that span. */
  service: string;
  /** Its name. */
  spanName: string;
  /** Its exception message, or else its status message, or else its name. */
  message: string;
}

/** How many spans of a trace one service sent. */
export interface TraceShare {
  /** The service. */
  service: string;
  /** Its spans in the trace. */
  spans: number;
}

/** One trace of the list, from its root span. */
export interface TraceItem {
  /** The trace ID, 32 lower-case hex characters. */
  traceId: string;
  /** The root span's ID. */
  rootSpanId: string;
  /** The root span's name, which names the request or job. */
  name: string;
  /** The service that received the request. */
  service: string;
  /** Its environment, or null. */
  environment: string | null;
  /** When it started, in ISO 8601 UTC. */
  start: string;
  /** How long it took, in milliseconds. */
  durationMs: number;
  /** Whether the request failed. */
  failed: boolean;
  /** How many spans of it have arrived. */
  spans: number;
  /** How many of them failed. */
  failedSpans: number;
  /** Why it failed, or null when no span failed. */
  cause: TraceCause | null;
  /** The services it went through, most spans first. */
  services: TraceShare[];
}

/** A page of the trace list, newest first. */
export interface TracesPage {
  /** The traces. */
  items: TraceItem[];
  /** Pass it as `cursor` for the next page; null on the last page. */
  nextCursor: string | null;
}

/** One trace on the duration chart. */
export interface TraceDurationPoint {
  /** The trace ID, so a click opens it. */
  traceId: string;
  /** When it started, in ISO 8601 UTC. */
  start: string;
  /** How long it took, in milliseconds. */
  durationMs: number;
  /** Whether it failed. */
  failed: boolean;
}

/** The durations of the newest traces of a window. */
export interface TraceDurations {
  /** Every failed and slow trace read, and an even sample of the rest, oldest first. */
  points: TraceDurationPoint[];
  /** How many traces were read. */
  read: number;
  /** The most one request reads; when `read` reaches it, older traces of the window are left out. */
  maxRead: number;
  /** The 95th percentile of the durations read, in milliseconds, or null when there were none. */
  p95Ms: number | null;
}

/** A service that received requests in the window. */
export interface TraceService {
  /** The service. */
  service: string;
  /** How many of the traces read it received. */
  traces: number;
}

/** A moment during a span; an exception is an event named `exception`. */
export interface TraceSpanEvent {
  /** When it happened, in ISO 8601 UTC. */
  timestamp: string;
  /** What happened. */
  name: string;
  /** Its attributes, such as `exception.message`. */
  attributes: Record<string, string>;
}

/** A pointer from a span to another span. */
export interface TraceSpanLink {
  /** The other span's trace. */
  traceId: string;
  /** The other span. */
  spanId: string;
  /** The link's attributes. */
  attributes: Record<string, string>;
}

/** One span of a trace. */
export interface TraceSpan {
  /** The span ID, 16 lower-case hex characters. */
  spanId: string;
  /** The parent's span ID, or null for the root. */
  parentSpanId: string | null;
  /** What the operation is. */
  name: string;
  /** What it does in its trace. */
  kind: SpanKind;
  /** When it started, in ISO 8601 UTC. */
  start: string;
  /** How long it took, in milliseconds. */
  durationMs: number;
  /** How it ended. */
  status: SpanStatus;
  /** Why it failed, when the sender said. */
  statusMessage: string | null;
  /** The service that sent it. */
  service: string;
  /** Its environment, or null. */
  environment: string | null;
  /** The host it ran on, or null. */
  host: string | null;
  /** Its attributes. */
  attributes: Record<string, string>;
  /** The attributes of the resource that sent it. */
  resourceAttributes: Record<string, string>;
  /** Moments during the span, oldest first. */
  events: TraceSpanEvent[];
  /** Pointers to other spans. */
  links: TraceSpanLink[];
  /** How much the sender or Veyra's limits left out. */
  dropped: { attributes: number; events: number; links: number };
}

/** A whole trace: every span that has arrived, by start time. */
export interface Trace {
  /** The trace ID. */
  traceId: string;
  /** The spans. */
  spans: TraceSpan[];
}

/** The sections of the Settings screen, in the order it shows them (ADR 0017). */
export type SettingSection = "Data" | "Rules" | "Email" | "Security" | "Ingestion" | "Advanced";

/** How a setting is edited. */
export type SettingKind = "WholeNumber" | "Number" | "Ratio" | "Duration" | "Switch" | "Text" | "Choice" | "Secret";

/** Where a setting's default comes from. */
export interface SettingSource {
  /** `code`, `file` (appsettings.json), `environment`, or `configuration` for any other source. */
  kind: "code" | "file" | "environment" | "configuration";
  /** The variable or file that sets it; null for the code. */
  name: string | null;
}

/** One setting on the Settings screen. Values are in configuration form: a ratio from 0 to 1, a duration d.hh:mm:ss. */
export interface Setting {
  /** The configuration key, such as `Veyra:Storage:LogRetentionDays`. */
  key: string;
  /** The section that shows it. */
  section: SettingSection;
  /** The card it sits on. */
  group: string;
  /** A short name. */
  label: string;
  /** What it does. */
  help: string;
  /** How it is edited. */
  kind: SettingKind;
  /** The unit shown; for a ratio the shown value is a percentage, for a duration this unit. */
  unit: string | null;
  /** The lowest value allowed, in the unit shown. */
  minimum: number | null;
  /** The highest value allowed, in the unit shown. */
  maximum: number | null;
  /** The values a choice can take. */
  choices: string[];
  /** What changing it can do beyond itself. */
  effect: "None" | "DeletesData" | "MayLockOut";
  /** Whether it may be left empty. */
  optional: boolean;
  /** Whether it shows only in the Advanced view. */
  advanced: boolean;
  /** The variable in deploy/.env that gives its first value. */
  environmentVariable: string | null;
  /** The value in effect; always null for a secret. */
  value: string | null;
  /** Whether it has a value; for a secret, the only thing shown. */
  isSet: boolean;
  /** The value without a saved one; always null for a secret. */
  default: string | null;
  /** Where the default comes from. */
  defaultSource: SettingSource;
  /** Whether a value is saved on the Settings screen. */
  saved: boolean;
  /** Who saved it last. */
  savedBy: string | null;
  /** When it was saved last, in ISO 8601. */
  savedAt: string | null;
}

/** A value set where Veyra is installed, shown read-only. */
export interface SystemSetting {
  /** What it is. */
  label: string;
  /** Its value, without passwords. */
  value: string;
  /** Where it is set. */
  source: string;
}

/** Every setting, and the version to save changes from. */
export interface SettingsPage {
  /** The settings version; a save from an older one is refused. */
  version: number;
  /** Every setting, in screen order. */
  settings: Setting[];
  /** What is set where Veyra is installed. */
  system: SystemSetting[];
}

/** One change to send: a value in configuration form, or null to reset to the default. */
export interface SettingChangeInput {
  /** The configuration key. */
  key: string;
  /** The new value; null resets it. */
  value: string | null;
}

/** The review of changes before they are saved. */
export interface SettingsReview {
  /** Why they cannot be saved; empty when they can. */
  problems: { key: string | null; message: string }[];
  /** What they will do. */
  notes: { key: string; text: string; risky: boolean }[];
  /** The changes that would change something, from and to in configuration form. */
  changes: { key: string; from: string | null; to: string | null }[];
  /** Whether saving needs an explicit confirmation. */
  confirmationRequired: boolean;
}

/** What kind of monitored system a change in the settings history is about. */
export type MonitoringSubject = "SqlServer" | "PostgresServer" | "Agent";

/** One change in the settings history: to a setting, or to a monitored system or agent (ADR 0019). */
export interface SettingHistoryItem {
  /** The change's number. */
  id: number;
  /** The setting, or the name of the monitored system. */
  key: string;
  /** Its label, its key when Veyra no longer has it, or the monitored system's name. */
  label: string;
  /** Its card, or the kind of monitored system, such as "SQL Server". */
  group: string | null;
  /** Its section; Databases or Agents for a monitored system. */
  section: SettingSection | "Databases" | "Agents" | null;
  /** The value before; null for a secret, a monitored system, or when none was saved. */
  oldValue: string | null;
  /** The value after; null for a secret, a monitored system, or when reset. */
  newValue: string | null;
  /** Whether its values are hidden. */
  secret: boolean;
  /** How it came to change; a monitored system is Added, Changed, or Removed. */
  kind: "Changed" | "Reset" | "Reverted" | "Imported" | "Added" | "Removed";
  /** The kind of monitored system; absent or null for a setting. */
  subject?: MonitoringSubject | null;
  /** For a monitored system, the fields a change touched, never their values. */
  fields?: string[] | null;
  /** The account that saved it, when it still exists. */
  changedBy: string | null;
  /** Who saved it. */
  changedByName: string;
  /** When, in ISO 8601. */
  changedAt: string;
  /** The imported file's name. */
  origin: string | null;
  /** Whether it can be set back. */
  canRevert: boolean;
}

/** A page of the settings history. */
export interface SettingsHistory {
  /** The changes, newest first. */
  changes: SettingHistoryItem[];
  /** The value of `before` for the next page; null on the last. */
  next: number | null;
}

/** Saved settings in a file, as exported and imported. */
export interface SettingsFile {
  /** Always `veyra-settings`. */
  format: string;
  /** The file format's version; 1. */
  formatVersion: number;
  /** When it was exported. */
  exportedAt: string | null;
  /** The saved values by key. */
  values: Record<string, string>;
}

/** Where a Veyra agent runs (ADR 0019). */
export type AgentPlacement = "BuiltIn" | "Remote";

/** How an agent stands; the first that applies wins, in this order. */
export type AgentState = "NotConnected" | "Offline" | "ConfigurationFailed" | "Unhealthy" | "Online";

/** A Veyra agent: the official OpenTelemetry Collector, run and configured by Veyra. */
export interface Agent {
  /** The agent's ID. */
  id: string;
  /** Its name, such as "Built in". */
  name: string;
  /** Where it runs. */
  placement: AgentPlacement;
  /** How it stands. */
  state: AgentState;
  /** The Collector's version, once the agent has said. */
  version: string | null;
  /** The machine or container it runs on, once the agent has said. */
  hostName: string | null;
  /** Its operating system, once the agent has said. */
  operatingSystem: string | null;
  /** When it last polled, in ISO 8601; null for never. */
  lastSeenAt: string | null;
  /** Why its configuration failed or its Collector is unhealthy, in the Collector's words. */
  error: string | null;
  /** How many SQL Servers it reads. */
  servers: number;
  /** When it was set up, in ISO 8601. */
  createdAt: string;
  /** Whether it has been installed: an agent added on the Agents page has no key until its installer used a code. */
  installed: boolean;
  /** When its open one-time code stops working, in ISO 8601; null when it has none. */
  codeExpiresAt: string | null;
  /** Whether it reads its host's CPU, memory, disks, and network. */
  host: boolean;
  /** Whether it reads the Windows Event Log's errors; Windows only. */
  eventLog: boolean;
  /** Whether it reads IIS; Windows only. */
  iis: boolean;
  /** Whether it reads the Docker containers' stats and logs; Linux only. */
  docker: boolean;
  /** The environment its host belongs to, such as `production`. */
  hostEnvironment: string;
}

/** A one-time code that installs an agent in another network. */
export interface AgentCode {
  /** The code, such as `K7QM-4TWX-9PRA`. Shown once: only its hash is kept. */
  code: string;
  /** When it stops working, in ISO 8601. */
  expiresAt: string;
}

/** An agent just added on the Agents page, with the code that installs it. */
export interface AddedAgent extends AgentCode {
  /** The agent, not installed yet. */
  agent: Agent;
}

/** How the agent's connection to a SQL Server is encrypted. */
export type SqlServerEncryption = "Required" | "TrustServerCertificate" | "Off";

/** The kind of SQL Server it is: one you run, an Azure SQL Managed Instance, or an Azure SQL Database. */
export type SqlServerPlatform = "SqlServer" | "ManagedInstance" | "AzureSqlDatabase";

/** How a SQL Server's monitoring stands; the first that applies wins, in this order. */
export type SqlServerState =
  | "AgentNotConnected"
  | "AgentOffline"
  | "ConfigurationFailed"
  | "LoginFailed"
  | "Unreachable"
  | "CertificateRejected"
  | "GrantMissing"
  | "Failing"
  | "Receiving"
  | "WaitingForData";

/** What every database server a Veyra agent reads has, SQL Server or PostgreSQL. Its password is never sent back. */
export interface MonitoredServer {
  /** The server's ID. */
  id: string;
  /** Its name in Veyra, its database block, such as `sql-prod-01`. */
  name: string;
  /** The host name or IP address the agent connects to. */
  address: string;
  /** The TCP port. */
  port: number;
  /** The environment its telemetry is filed under. */
  environment: string;
  /** The agent that reads it. */
  agentId: string;
  /** That agent's name. */
  agentName: string;
  /** The SQL login or PostgreSQL role the agent signs in with. */
  login: string;
  /** How the connection is encrypted. */
  encryption: SqlServerEncryption;
  /** How its monitoring stands. */
  state: SqlServerState;
  /** The error behind the state, as the agent or the server gave it. */
  detail: string | null;
  /** When that error or the last data was seen, in ISO 8601. */
  since: string | null;
  /** When it was added, in ISO 8601. */
  createdAt: string;
  /** When it was last changed, in ISO 8601. */
  changedAt: string;
}

/** A SQL Server a Veyra agent reads. Its password is never sent back. */
export interface SqlServer extends MonitoredServer {
  /** The kind of SQL Server it is. */
  platform: SqlServerPlatform;
  /** The one database it is read in, for an Azure SQL Database; otherwise null. */
  database: string | null;
}

/**
 * How the agent's connection to a PostgreSQL server is encrypted: `Required` checks the certificate and the server's
 * name (sslmode verify-full), `TrustServerCertificate` takes any certificate (require), `Off` sends no TLS (disable).
 */
export type PostgresEncryption = SqlServerEncryption;

/**
 * A PostgreSQL server a Veyra agent reads (VEY-33), with the same fields and states as a SQL Server. Its login is the
 * role the agent signs in as. Its password is never sent back.
 */
export type PostgresServer = MonitoredServer;

/** A PostgreSQL server as the Settings screen sends it, to add one or to change one. */
export interface PostgresServerInput {
  /** Its name in Veyra: letters, digits, dots, dashes, and underscores, at most 63. */
  name: string;
  /** The host name or IP address the agent connects to. */
  address: string;
  /** The TCP port, from 1 to 65535. */
  port: number;
  /** The environment its telemetry is filed under, such as `production`. */
  environment: string;
  /** The agent that reads it; null for the built-in agent. */
  agentId: string | null;
  /** The role the agent signs in as: letters, digits, underscores, dots, and dashes. */
  login: string;
  /** The role's password, without spaces, quotes, or backslashes; null when changing keeps the stored one. */
  password: string | null;
  /** How the connection is encrypted. */
  encryption: PostgresEncryption;
}

/** A SQL Server as the Settings screen sends it, to add one or to change one. */
export interface SqlServerInput {
  /** Its name in Veyra: letters, digits, dots, dashes, and underscores, at most 63. */
  name: string;
  /** The host name or IP address the agent connects to. */
  address: string;
  /** The TCP port, from 1 to 65535. */
  port: number;
  /** The environment its telemetry is filed under, such as `production`. */
  environment: string;
  /** The agent that reads it; null for the built-in agent. */
  agentId: string | null;
  /** The SQL login the agent signs in with. */
  login: string;
  /** The login's password; null when changing a server keeps the stored one. */
  password: string | null;
  /** How the connection is encrypted. */
  encryption: SqlServerEncryption;
  /** The kind of SQL Server it is. */
  platform: SqlServerPlatform;
  /** The database to read, for an Azure SQL Database; null for the other kinds. */
  database: string | null;
}

/** A wait type explained (ADR 0020): its category, words for the Simple view, and what it means and usually causes. */
export interface WaitTerm {
  /** The wait type, such as `LCK_M_U`. */
  type: string;
  /** Its category, such as Lock or Disk I/O. */
  category: string;
  /** A few words for the Simple view, such as "waits for a lock". */
  simple: string;
  /** What it means. */
  meaning: string;
  /** What usually causes it. */
  causes: string;
}

/** Something a finding offers to do (ADR 0020). Veyra never changes a server; it offers what a person runs. */
export interface FindingAction {
  /** `copy` a statement, open a program's `logs`, or open a `session`. */
  kind: "copy" | "logs" | "session";
  /** The button's words. */
  label: string;
  /** The statement, the program, or the session ID. */
  value: string;
  /** For `copy`, who should run it and what it does. */
  hint: string | null;
}

/** One thing Veyra found on a database server, in words for anyone and in SQL Server's terms (ADR 0020). */
export interface Finding {
  /** What kind of finding, such as `blocking`. */
  kind: string;
  /** How bad it is. */
  severity: "ok" | "caution" | "warning";
  /** One sentence, for anyone. */
  title: string;
  /** Why it matters and what usually causes it. */
  detail: string;
  /** The same in SQL Server's terms. */
  advanced: string;
  /** The session it is about, when it is about one. */
  session: number | null;
  /** What can be done. */
  actions: FindingAction[];
}

/** A session in a blocking chain, with the sessions that wait for it. */
export interface BlockingNode {
  /** The session's ID; its details are among the live view's sessions. */
  session: number;
  /** How many wait for it, directly or through others. */
  waiting: number;
  /** The sessions that wait for it directly, the longest waiting first. */
  children: BlockingNode[];
}

/** One busy session of a SQL Server in its live view (ADR 0020). */
export interface LiveSession {
  /** SQL Server's session ID. */
  id: number;
  /** `running`, `runnable`, or `suspended` for a request; `sleeping` without one. */
  status: "running" | "runnable" | "suspended" | "sleeping";
  /** The request's command, such as `UPDATE`; empty without a request. */
  command: string;
  /** The database it works in. */
  database: string;
  /** The client program. */
  program: string;
  /** The client machine. */
  host: string;
  /** The login. */
  login: string;
  /** The session it waits for, or null. */
  blockedBy: number | null;
  /** How many wait for it, directly or through others. */
  blocking: number;
  /** What it waits on, or null. */
  waitType: string | null;
  /** How long it has waited on that, in seconds. */
  waitSeconds: number;
  /** The resource it waits for, or null. */
  waitResource: string | null;
  /** How long the request has run, in seconds; 0 without a request. */
  elapsedSeconds: number;
  /** CPU time in seconds. */
  cpuSeconds: number;
  /** Logical reads. */
  reads: number;
  /** Writes. */
  writes: number;
  /** Space it holds in tempdb. */
  tempdbBytes: number;
  /** Memory granted to the request. */
  grantBytes: number;
  /** Memory the request asked for. */
  grantRequestedBytes: number;
  /** Whether the request still waits for its memory. */
  grantWaiting: boolean;
  /** How long its oldest transaction has been open, in seconds, or null without one. */
  transactionSeconds: number | null;
  /** Whether its open transaction changed data. */
  changedData: boolean;
  /** How many transactions it has open. */
  openTransactions: number;
  /** Its isolation level in words. */
  isolation: string;
  /** How far a long operation has come, from 0 to 100, or null. */
  percentComplete: number | null;
  /** SQL Server's estimate of the time a long operation still needs, in seconds. */
  remainingSeconds: number;
  /** The statement's query hash, or null. */
  queryHash: string | null;
  /** The statement's plan hash, or null. */
  planHash: string | null;
  /** Seconds since its last request ended. */
  idleSeconds: number;
  /** Seconds since it logged in. */
  connectedSeconds: number;
  /** What the statement does, in words, such as "Update of Orders by CustomerId". */
  name: string;
  /** The statement running now; without a request, the last batch it sent. */
  text: string;
}

/** The numbers at the top of the Live tab. */
export interface LiveTiles {
  /** Batch requests per second now, or null. */
  batchRequests: number | null;
  /** Batch requests per second over the last 15 minutes. */
  batchRequestsLine: ChartLine;
  /** Sessions waiting for another session now. */
  blocked: number;
  /** Blocked sessions over the last 15 minutes. */
  blockedLine: ChartLine;
  /** The longest running request now, in seconds, or 0. */
  longestRequestSeconds: number;
  /** The oldest open transaction now, in seconds, or null. */
  oldestTransactionSeconds: number | null;
  /** The session that holds it. */
  oldestTransactionSession: number | null;
  /** Requests waiting for memory to run. */
  grantsWaiting: number;
  /** The share of tempdb still free, from 0 to 1, or null. */
  tempdbFreeRatio: number | null;
  /** The size of tempdb's data files. */
  tempdbSizeBytes: number;
}

/** A SQL Server's Live tab (ADR 0020): its newest read, or the read at a chosen moment. */
export interface DatabaseLive {
  /** The engine. */
  engine: string;
  /** The server's name. */
  server: string;
  /** Its environment. */
  environment: string | null;
  /** When the read was made, or null when there is none. */
  sampledAt: string | null;
  /** How old the read is, in seconds. */
  ageSeconds: number | null;
  /** The moment asked for, or null for now. */
  at: string | null;
  /** The numbers at the top. */
  tiles: LiveTiles;
  /** What Veyra found, the worst first. */
  findings: Finding[];
  /** The blocking chains. */
  blocking: BlockingNode[];
  /** The busy sessions, the longest running first. */
  sessions: LiveSession[];
  /** Every wait type the sessions wait on, explained. */
  waits: Record<string, WaitTerm>;
}

/** A problem a plan or a statement's history shows, with what to do (ADR 0020, area 4). */
export interface PlanWarning {
  /** `implicit-conversion`, `spill`, `no-join-predicate`, `missing-index`, or `regression`. */
  kind: string;
  /** How bad it is. */
  severity: "caution" | "warning";
  /** One line. */
  title: string;
  /** Why it matters. */
  detail: string;
  /** What to do, in words. */
  fix: string;
  /** A statement for a DBA to run, or null. */
  script: string | null;
}

/** One statement over the range, for the Queries tab. */
export interface QueryRow {
  /** Its query hash, which opens its page. */
  queryHash: string;
  /** Its database, or null. */
  database: string | null;
  /** What it does, in words. */
  name: string;
  /** The statement. */
  text: string;
  /** Its time over the range, all runs together. */
  totalSeconds: number;
  /** Runs over the range. */
  executions: number;
  /** CPU over the range. */
  cpuSeconds: number;
  /** Logical reads over the range. */
  reads: number;
  /** Time per run. */
  perRunSeconds: number;
  /** How many plans it used. */
  plans: number;
  /** What its plans and history show. */
  findings: PlanWarning[];
}

/** A SQL Server's Queries tab. */
export interface DatabaseQueries {
  /** The range. */
  range: string;
  /** The statements by total time; Veyra's agent left out. */
  statements: QueryRow[];
  /** Statements of Veyra's agent. */
  agentStatements: number;
  /** Their time. */
  agentSeconds: number;
  /** Each user database and whether it keeps Query Store. */
  queryStore: QueryStoreState[];
}

/** Whether a database keeps Query Store (ADR 0020). */
export interface QueryStoreState {
  /** The database. */
  database: string;
  /** Whether it is on. */
  on: boolean;
  /** The statement a DBA runs to turn it on, or null. */
  script: string | null;
}

/** A statement's plan in one Query Store interval. */
export interface QueryStoreInterval {
  /** Start. */
  start: string;
  /** End. */
  end: string;
  /** The plan hash. */
  plan: string;
  /** Runs. */
  runs: number;
  /** Seconds per run. */
  perRunSeconds: number;
  /** CPU seconds per run. */
  cpuPerRunSeconds: number;
  /** Reads per run. */
  readsPerRun: number;
  /** The wait category it waited on most. */
  topWait: string;
  /** Its waits, in seconds. */
  waitSeconds: number;
}

/** What a statement waited on, by category. */
export interface QueryWait {
  /** The category. */
  category: string;
  /** Seconds. */
  seconds: number;
}

/** One operator of a plan. */
export interface PlanOperator {
  /** Depth in the plan's tree. */
  depth: number;
  /** What it does, such as Key Lookup. */
  physical: string;
  /** Its logical operation. */
  logical: string;
  /** The table and index, or null. */
  target: string | null;
  /** Its own share of the plan's cost, from 0 to 1. */
  costShare: number;
  /** Rows expected. */
  estimatedRows: number;
  /** Whether the plan warns about it. */
  warned: boolean;
}

/** A plan a statement used. */
export interface QueryPlan {
  /** The plan hash. */
  hash: string;
  /** Runs on it. */
  runs: number;
  /** Seconds per run. */
  perRunSeconds: number;
  /** CPU seconds per run. */
  cpuPerRunSeconds: number;
  /** Reads per run. */
  readsPerRun: number;
  /** First reported. */
  firstSeen: string;
  /** Last reported. */
  lastSeen: string;
  /** Whether Veyra holds its XML. */
  captured: boolean;
  /** Its operators. */
  operators: PlanOperator[];
  /** Its warnings. */
  warnings: PlanWarning[];
}

/** A statement in one bucket. */
export interface QueryPoint {
  /** The bucket's start. */
  start: string;
  /** Runs. */
  runs: number;
  /** Seconds per run, or null. */
  perRunSeconds: number | null;
  /** CPU seconds per run, or null. */
  cpuPerRunSeconds: number | null;
  /** Reads per run, or null. */
  readsPerRun: number | null;
  /** The plan that ran most, or null. */
  plan: string | null;
}

/** A program and machine that ran a statement. */
export interface SessionCaller {
  /** The program. */
  program: string;
  /** The machine. */
  host: string;
  /** The login. */
  login: string;
  /** How many live reads saw it. */
  samples: number;
}

/** A statement's page (ADR 0020, area 4). */
export interface DatabaseQuery {
  /** Its query hash. */
  queryHash: string;
  /** Its database, or null. */
  database: string | null;
  /** What it does, in words. */
  name: string;
  /** The statement. */
  text: string;
  /** The range. */
  range: string;
  /** Its totals. */
  totals: QueryRow;
  /** Each bucket. */
  points: QueryPoint[];
  /** Its plans, the latest first. */
  plans: QueryPlan[];
  /** What its plans and history show. */
  findings: PlanWarning[];
  /** Who runs it. */
  callers: SessionCaller[];
  /** Its Query Store intervals, oldest first. */
  history: QueryStoreInterval[];
  /** What it waited on, the most first. */
  waits: QueryWait[];
}

/** A blocking episode: one session holding others up (ADR 0020, area 5). */
export interface BlockingEpisode {
  /** The first read that saw it. */
  start: string;
  /** The last read that saw it. */
  end: string;
  /** The head blocker's session. */
  head: number;
  /** Its program. */
  program: string;
  /** Its machine. */
  host: string;
  /** Whether it slept with an open transaction. */
  sleeping: boolean;
  /** What its statement does, in words. */
  headName: string;
  /** Its statement or last batch. */
  headText: string;
  /** The most sessions waiting in one read. */
  mostWaiting: number;
  /** The longest wait, in seconds. */
  longestWaitSeconds: number;
  /** The programs that waited. */
  waitingPrograms: string[];
  /** One sentence for anyone. */
  summary: string;
}

/** A session caught in a deadlock. */
export interface DeadlockProcess {
  /** Its ID in the graph. */
  id: string;
  /** Its session. */
  session: number;
  /** Its program. */
  program: string;
  /** Its machine. */
  host: string;
  /** Its login. */
  login: string;
  /** Its database. */
  database: string;
  /** Whether SQL Server ended it. */
  victim: boolean;
  /** The lock it waited for. */
  waits: string;
  /** What its statement does, in words. */
  name: string;
  /** Its statement. */
  text: string;
}

/** A locked resource of a deadlock. */
export interface DeadlockResource {
  /** What is locked. */
  kind: string;
  /** The table and index. */
  target: string;
  /** Who held it, with the mode. */
  holders: string[];
  /** Who waited for it, with the mode. */
  waiters: string[];
}

/** A deadlock SQL Server broke. */
export interface Deadlock {
  /** What identifies it. */
  key: string;
  /** When. */
  at: string;
  /** One sentence for anyone. */
  summary: string;
  /** The sessions. */
  processes: DeadlockProcess[];
  /** The resources. */
  resources: DeadlockResource[];
}

/** A SQL Server's Blocking tab. */
export interface DatabaseBlocking {
  /** The range. */
  range: string;
  /** The episodes, the newest first. */
  episodes: BlockingEpisode[];
  /** The deadlocks, the newest first. */
  deadlocks: Deadlock[];
}

/** One data or log file of a database (ADR 0020). */
export interface DatabaseFile {
  /** Its logical name. */
  name: string;
  /** `data` or `log`. */
  type: string;
  /** Its volume. */
  volume: string;
  /** Its size. */
  sizeBytes: number;
  /** How it grows. */
  growth: string;
  /** Its maximum. */
  maxSize: string;
}

/** One database in full (ADR 0020, areas 6 to 8). */
export interface DatabaseStorage {
  /** Its name. */
  name: string;
  /** Its state. */
  state: string;
  /** Its recovery model. */
  recoveryModel: string;
  /** Its compatibility level. */
  compatibility: string;
  /** Its page verify option. */
  pageVerify: string;
  /** Whether auto close is on. */
  autoClose: boolean;
  /** Whether auto shrink is on. */
  autoShrink: boolean;
  /** Whether read committed snapshot is on. */
  readCommittedSnapshot: boolean;
  /** Whether it is encrypted. */
  encrypted: boolean;
  /** What its log waits for to be reused. */
  logReuseWait: string;
  /** The share of its log in use, or null. */
  logUsedRatio: number | null;
  /** Its virtual log files, or null. */
  virtualLogFiles: number | null;
  /** Its files. */
  files: DatabaseFile[];
  /** Seconds since its last full backup, or null. */
  fullBackupAgeSeconds: number | null;
  /** Seconds since its last differential backup, or null. */
  diffBackupAgeSeconds: number | null;
  /** Seconds since its last log backup, or null in simple recovery. */
  logBackupAgeSeconds: number | null;
  /** Whether its log chain has a gap. */
  logChainBroken: boolean;
  /** How long its last full backup took, or null. */
  lastFullSeconds: number | null;
  /** How large it was, or null. */
  lastFullBytes: number | null;
  /** Where it went, or null. */
  lastFullDestination: string | null;
  /** Seconds since its last good CHECKDB, or null. */
  checkdbAgeSeconds: number | null;
  /** Damaged pages not yet repaired. */
  damagedPages: number;
  /** What Veyra makes of it. */
  findings: PlanWarning[];
}

/** A volume that holds database files. */
export interface DatabaseVolume {
  /** Its mount point. */
  volume: string;
  /** Its size. */
  sizeBytes: number;
  /** Its free space. */
  freeBytes: number;
  /** The share in use. */
  usedRatio: number;
  /** Days until full at the pace of the last week, or null. */
  daysUntilFull: number | null;
}

/** A SQL Server's databases and volumes in full. */
export interface StorageView {
  /** Its databases. */
  databases: DatabaseStorage[];
  /** Its volumes. */
  volumes: DatabaseVolume[];
  /** Server-wide findings. */
  findings: PlanWarning[];
}

/** One index with its use since SQL Server started (ADR 0020, area 9). */
export interface IndexInfo {
  /** The database. */
  database: string;
  /** The table. */
  table: string;
  /** The index. */
  name: string;
  /** Its type. */
  type: string;
  /** Its key columns. */
  keys: string;
  /** Its included columns. */
  include: string;
  /** Its size. */
  sizeBytes: number;
  /** Reads by users. */
  reads: number;
  /** Writes by users. */
  writes: number;
  /** Lock escalations. */
  escalations: number;
}

/** A SQL Server's index and statistics advice. */
export interface IndexAdvice {
  /** The findings, with the statements to run. */
  findings: PlanWarning[];
  /** The largest indexes. */
  indexes: IndexInfo[];
}

/** A wait type over the range, explained (ADR 0020, area 2). */
export interface WaitTypeTotal {
  /** The type explained. */
  term: WaitTerm;
  /** Seconds waited on it over the range. */
  seconds: number;
}

/** A SQL Server as a whole (ADR 0020, areas 1, 2, and 14). */
export interface ServerView {
  /** Its version. */
  version: string;
  /** Its cumulative update. */
  updateLevel: string;
  /** Its edition. */
  edition: string;
  /** How long it has run, in seconds, or null. */
  uptimeSeconds: number | null;
  /** Whether the version is out of support. */
  unsupported: boolean;
  /** SQL Server's share of the CPU now, or null. */
  sqlCpu: number | null;
  /** Others' share of the CPU now, or null. */
  otherCpu: number | null;
  /** Tasks waiting for a CPU, or null. */
  runnable: number | null;
  /** The share of worker threads in use, or null. */
  workers: number | null;
  /** The memory SQL Server aims to use, or null. */
  memoryTarget: number | null;
  /** The memory it uses, or null. */
  memoryCommitted: number | null;
  /** The machine's free memory, or null. */
  osMemoryFree: number | null;
  /** Whether the machine signals low memory. */
  osMemoryLow: boolean;
  /** Requests waiting for memory, or null. */
  grantsWaiting: number | null;
  /** Memory dumps in the last day, or null. */
  dumpsDay: number | null;
  /** Whether SQL Server Agent does not run; null when there is none. */
  agentStopped: boolean | null;
  /** Batch requests per second now, or null. */
  requests: number | null;
  /** Usual for this hour of the week, or null. */
  requestsUsual: number | null;
  /** SQL Server's CPU over the range. */
  sqlCpuLine: ChartLine;
  /** Others' CPU over the range. */
  otherCpuLine: ChartLine;
  /** The wait types with the most time, explained. */
  waits: WaitTypeTotal[];
  /** What Veyra makes of it. */
  findings: PlanWarning[];
}

/** One step of a SQL Agent job run. */
export interface AgentJobStep {
  /** Its number. */
  id: number;
  /** Its name. */
  name: string;
  /** Whether it succeeded. */
  succeeded: boolean;
  /** How long it took, in seconds. */
  seconds: number;
  /** Its message. */
  message: string;
}

/** One run of a SQL Agent job. */
export interface AgentJobRun {
  /** When it started. */
  at: string;
  /** Whether it succeeded. */
  succeeded: boolean;
  /** How long it took, in seconds. */
  seconds: number;
  /** SQL Server Agent's message. */
  message: string;
  /** Its steps. */
  steps: AgentJobStep[];
}

/** A SQL Agent job in full (ADR 0020, area 10). */
export interface AgentJobView {
  /** The job. */
  name: string;
  /** Whether its last run failed. */
  failed: boolean;
  /** When its last run started, or null. */
  lastRun: string | null;
  /** How long it took, or null. */
  lastSeconds: number | null;
  /** How long it has run now, or null. */
  runningSeconds: number | null;
  /** When it runs next, or null. */
  nextRun: string | null;
  /** Whether it did not start when scheduled. */
  late: boolean;
  /** Its usual duration, or null. */
  usualSeconds: number | null;
  /** Its runs of the last week. */
  runs: AgentJobRun[];
}

/** A SQL Server event from system_health (ADR 0020, area 11). */
export interface ServerEvent {
  /** When. */
  at: string;
  /** The event's name. */
  name: string;
  /** How bad it is. */
  severity: "ok" | "caution" | "warning";
  /** What it means, for anyone. */
  words: string;
  /** The error number, or 0. */
  error: number;
  /** SQL Server's severity, or 0. */
  errorSeverity: number;
  /** The wait type of a long wait. */
  wait: string;
  /** SQL Server's message. */
  message: string;
}

/** A server's availability (ADR 0020, area 12). */
export interface AvailabilityView {
  /** Its Availability Group replicas. */
  replicas: { group: string; replica: string; role: string; healthy: boolean }[];
  /** Its Availability Group databases with their queues. */
  databases: {
    group: string;
    replica: string;
    database: string;
    queueBytes: number;
    lagSeconds: number;
    synchronizing: boolean;
  }[];
  /** Its log-shipped databases. */
  logShipping: { database: string; side: string; behind: boolean; delaySeconds: number | null }[];
  /** The nodes of its failover cluster. */
  nodes: { node: string; state: string; owner: boolean }[];
}

/** A server's configuration and security posture (ADR 0020, area 13). */
export interface ConfigurationView {
  /** The settings worth knowing. */
  settings: { name: string; value: string; recommended: string }[];
  /** Settings off their recommended values and security findings, with their fix. */
  findings: PlanWarning[];
  /** The members of the sysadmin role. */
  sysadmins: string[];
  /** Changes of the last 30 days, the newest first. */
  changes: { at: string; item: string; from: string; to: string }[];
}

/** A permission the monitoring login lacks (VEY-58). */
export interface PermissionGap {
  /** The permission, such as `VIEW SERVER STATE` or `SELECT ON msdb.dbo.sysjobs`. */
  permission: string;
  /** The tabs it leaves empty, by their names in the address. */
  tabs: string[];
  /** What Veyra cannot read without it, in words. */
  reads: string;
}

/** What the monitoring login may not read on a server, and the statements that grant it (VEY-58). */
export interface PermissionsView {
  /** Whether the agent reported the login's permissions within the last 15 minutes. */
  reported: boolean;
  /** The login the agent signs in as, or null when nothing was reported. */
  login: string | null;
  /** What the agent reads: `sqlserver`, `managed-instance`, or `azure-sql-database`; null when nothing was reported. */
  platform: string | null;
  /** Each missing permission with the tabs it leaves empty. */
  gaps: PermissionGap[];
  /** The statements that grant every missing permission, or null for none. */
  script: string | null;
}

/** What an installed agent reads of the machine it runs on. */
export interface AgentHostInput {
  /** Whether to read the host's CPU, memory, disks, and network. */
  host: boolean;
  /** Whether to read the Windows Event Log's errors; Windows only, and only with the host. */
  eventLog: boolean;
  /** Whether to read IIS; Windows only, and only with the host. */
  iis: boolean;
  /** Whether to read the Docker containers' stats and logs; Linux only, and only with the host. */
  docker: boolean;
  /** The environment the host belongs to: letters, digits, dots, dashes, underscores. */
  environment: string;
}
