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

/** How serious an open alert is. Red is warning, amber is caution, on every screen (ADR 0007). */
export type AlertSeverity = "Caution" | "Warning";

/**
 * The built-in alert rules (ADR 0012) and the unit of their measurements: `error-rate` a ratio of records from 0 to 1,
 * `latency-p95` seconds, `disk-space` a share of a disk from 0 to 1, and `disk-filling` days until a disk is full.
 */
export type AlertRule = "error-rate" | "latency-p95" | "disk-space" | "disk-filling";

/** What a block is: a service seen in its logs or metrics, or a host. */
export type BlockKind = "service" | "host";

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
  /** The start of the period, midnight UTC. */
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
  /** One entry per UTC day, oldest first. */
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
