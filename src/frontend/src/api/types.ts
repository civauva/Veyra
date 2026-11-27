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
  /** Which look of the app to draw (ADR 0021). */
  look: LookChoice;
}

/** The person's look, the instance's default, and whether people may choose (VEY-71). */
export interface LookChoice {
  /** The look the person picked, or null when they have not; kept even while choosing is off. */
  chosen: string | null;
  /** The look an Admin set for the instance. */
  default: string;
  /** Whether people may choose; when not, everyone sees the default. */
  mayChoose: boolean;
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
  /** The host that wrote it, from the resource (ADR 0024); null or missing when the sender named none. */
  hostName?: string | null;
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
  /** Matching records per level, by severity name (ADR 0024); missing levels have none. */
  levels?: Partial<Record<Severity, number>>;
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
  /** The matching records of the whole window, added up on the server (VEY-68). */
  total: number;
  /** Those at Error or worse (VEY-68). */
  errors: number;
  /** The matching records of the whole window per level, by severity name (ADR 0024). */
  levels?: Partial<Record<Severity, number>>;
}

/** The field a term of a log query filters on (ADR 0024). */
export type LogQueryField = "service" | "environment" | "host" | "level" | "trace" | "span" | "attribute" | "text";

/** One term of a log query, as the server located it in the text (`GET /api/v1/logs/parse`). */
export interface LogQueryTerm {
  /** What the term filters on; `text` for a word or a quoted phrase of the message. */
  field: LogQueryField;
  /** `:` for "is", `>=` for "this level or worse"; null for a word or phrase of the message. */
  op: ":" | ">=" | null;
  /** The value, without quotes. */
  value: string;
  /** The attribute's name, for an attribute term; null for the others. */
  key?: string | null;
  /** Where the term starts in the text, counted in characters from 0. */
  start: number;
  /** Where it ends, exclusive. */
  end: number;
}

/** The terms of a parsed log query. */
export interface LogQueryParse {
  /** The terms in the order written. */
  terms: LogQueryTerm[];
}

/** A saved view of a screen's query (ADR 0024): the query, not the time range. */
export interface SavedView {
  /** The view's ID. */
  id: string;
  /** Its name. */
  name: string;
  /** The screen it belongs to; `logs` in this version. */
  screen: string;
  /** The query text. */
  query: string;
  /** Whether everyone may open it. */
  shared: boolean;
  /** Whether the signed-in person owns it, and so may change or delete it. */
  mine: boolean;
  /** Who owns it, for a view shared by someone else. */
  ownerName: string | null;
}

/** What a person sends to save a view. */
export interface SavedViewInput {
  /** The name, 1 to 80 characters. */
  name: string;
  /** The screen it belongs to. */
  screen: string;
  /** The query text. */
  query: string;
  /** Whether everyone may open it. */
  shared: boolean;
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
  /** What its alert means and why it matters, with `{server}` and `{part}` to fill in (VEY-60). */
  meaning: string;
  /** What to do to resolve its alert, in order. */
  steps: FixStep[];
}

/**
 * One thing a person does to resolve an alert (VEY-60). In a script, `{part}` is filled in so that it is safe inside
 * `N'…'`, and `[{part}]` as a bracketed name.
 */
export interface FixStep {
  /** What to do and why, with `{server}` and `{part}` to fill in. */
  text: string;
  /** A query or command to run, or null. */
  script: string | null;
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

/** The environment switcher's list from `GET /api/v1/home/environments` (VEY-67). */
export interface EnvironmentChoices {
  /** The choice of every environment together, with the worst weather of any; null when the search leaves it out. */
  all: { condition: ConditionKind } | null;
  /** The environments whose names hold the search, by name. */
  environments: HomeEnvironment[];
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
  hourly: {
    observed: HistogramBucket[];
    expected: ExpectedHour[];
    daysOfHistory: number;
    /** The records of the observed hours, added up on the server (VEY-68). */
    records: number;
    /** The records at Error or worse of the observed hours (VEY-68). */
    errors: number;
    /** The average records of an hour past, without the hour still filling, which scales the expected hours. */
    averageRecords: number;
  };
  /** Log storage and its outlook. */
  storage: HomeStorage;
  /** Every disk watched through host metrics: those that fill soonest first, then the fullest. */
  disks: HomeDisk[];
  /** What the dashboards show of the estate, worked out on the server (VEY-68). */
  estate: HomeEstate;
}

/** How many blocks are at each health. */
export interface HealthCounts {
  /** With an open warning. */
  warning: number;
  /** With an open caution and no warning. */
  caution: number;
  /** Without an open alert. */
  ok: number;
  /** Whose health is not known. */
  unknown: number;
}

/** The home's estate, outlook, and counts, worked out on the server so the dashboards only lay them out (VEY-68). */
export interface HomeEstate {
  /** The services, by name. */
  services: HomeBlock[];
  /** The same services, warnings first, then cautions, then the rest, for a home with room for only some. */
  servicesWorstFirst: HomeBlock[];
  /** How many services are at each health. */
  serviceCounts: HealthCounts;
  /** The hosts that have an open alert or report disks, by name. */
  hosts: HomeHostTile[];
  /** How many hosts are at each health. */
  hostCounts: HealthCounts;
  /** The database servers that have an open alert, by name. */
  databases: HomeBlock[];
  /** How many database servers are at each health. */
  databaseCounts: HealthCounts;
  /** The disks that fill within 30 days and have no open alert. */
  diskOutlook: HomeDiskOutlook;
  /** How many of the open alerts the home tells are warnings, and how many cautions. */
  alertCounts: { warnings: number; cautions: number };
}

/** One host tile of the home. */
export interface HomeHostTile {
  /** The host name. */
  name: string;
  /** Its worst state from its open alerts. */
  health: BlockHealth;
  /** The disk it draws: the one that fills soonest within 30 days, or else its fullest; null without disk metrics. */
  disk: HomeDisk | null;
  /** Whether that disk fills within 30 days, so the tile says when instead of how full. */
  filling: boolean;
}

/** The home's disk outlook. */
export interface HomeDiskOutlook {
  /** The disks that fill within 30 days and have no open alert, the soonest first. */
  filling: HomeDisk[];
  /** How many hosts report disks. */
  hosts: number;
  /** Whether disks fill within 30 days but every one has an open alert, which Needs you tells instead. */
  allAlerted: boolean;
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
  /** The channels it told, each once, in the order they were first told (VEY-68). */
  told: { id: string; name: string }[];
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
  /**
   * For a database server's severe error, memory error, and failed login rules, the errors they counted, newest
   * first; otherwise null or absent (VEY-61).
   */
  serverErrors?: ServerError[] | null;
  /** The incident the alert belongs to (ADR 0026); null or absent when it belongs to none. */
  incidentId?: string | null;
  /** That incident's key, such as "INC-42". */
  incidentKey?: string | null;
}

/** One error a database server's error alert counted (VEY-61). */
export interface ServerError {
  /** When the server was read with it, within a minute of when it happened. */
  at: string;
  /** SQL Server's error number, such as 824. */
  error: number;
  /** Its severity, from 0 to 25. */
  severity: number;
  /** What SQL Server said, or what Veyra says of an event without a message. */
  message: string;
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
  /**
   * For a database server, the area of its screen the alert belongs to, such as `blocking` (VEY-68); null for other
   * blocks.
   */
  area: string | null;
  /** The incident the alert belongs to (ADR 0026); null when it belongs to none. */
  incidentId?: string | null;
  /** That incident's key, such as "INC-42". */
  incidentKey?: string | null;
}

/** How one area of a database server is, from its open alerts, as `GET /api/v1/alerts/areas` ranks it (VEY-68). */
export interface AlertAreaState {
  /** The area, by its tab's name in the address. */
  area: string;
  /** Warning, Caution, or Ok, from its worst open alert. */
  health: BlockHealth;
  /** Its open alerts, the warnings first. */
  alerts: AlertListItem[];
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
  /** Only alerts about this kind of block. */
  kind?: BlockKind;
  /** Only alerts about the block of exactly this name, for a block's own screen (VEY-68). */
  block?: string;
  /** Only alerts about exactly this part, such as a disk or a container's host (VEY-68). */
  part?: string;
  /** `worst` lists warnings first, each the oldest first; the newest first otherwise (VEY-68). */
  order?: "newest" | "worst";
  /** The most alerts on the page, up to 200; 50 when left out. */
  limit?: number;
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

/** A range of the shared time (ADR 0022) that ends now. */
export type MetricPreset = "15m" | "1h" | "6h" | "24h" | "7d" | "30d";

/**
 * The range a metrics screen shows (ADR 0022): a preset ending now, or a window of the person's own written
 * `from~to`, two ISO instants, so it stays one string that keys and links can carry.
 */
export type MetricRange = MetricPreset | `${string}~${string}`;

/** The range a metrics answer covers: the preset asked for, or `custom` for a window of `from` and `to`. */
export type AnsweredRange = MetricPreset | "custom";

/** The figures over the Metrics overview's cards (VEY-67). */
export interface MetricsSummary {
  /** Hosts listed. */
  hosts: number;
  /** Listed hosts with a disk that is full within 30 days at the pace of its last day. */
  fillingDisks: number;
  /** Services listed. */
  services: number;
  /** Database servers listed. */
  databases: number;
  /** Containers listed. */
  containers: number;
  /** Listed containers that restarted within the range. */
  restarting: number;
  /** Whether anything sent metrics recently, listed or not: false shows how to start sending. */
  anySent: boolean;
}

/** What the Metrics overview is asked for (VEY-67); the server keeps only the cards that match. */
export interface MetricsOverviewChoices {
  /** Text a card's name must hold, in any case; empty for every card. */
  search: string;
  /** The environment chosen in the top bar, or null for all. */
  environment: string | null;
}

/** The Metrics overview from `GET /api/v1/metrics/overview`: the cards that match its choices. */
export interface MetricsOverview {
  /** When it was put together. */
  at: string;
  /** The window of the lines. */
  range: AnsweredRange;
  /** The start of the window the answer covers, as an ISO instant (ADR 0022). */
  from: string;
  /** The end of the window the answer covers: now, or the `to` asked for (ADR 0022). */
  to: string;
  /** The width of each point. */
  bucketSeconds: number;
  /** How much metric data is stored. */
  usage: MetricUsage;
  /** The figures over the cards, counted by the server over the cards it lists (VEY-67). */
  summary: MetricsSummary;
  /** The hosts that match the search and the environment. */
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
  /** Whether the engine keeps it for itself, such as master or postgres (VEY-64); hidden unless asked. */
  system: boolean;
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
  /** Whether it is a catalog table, or in a database the engine keeps for itself (VEY-64). */
  system: boolean;
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
  /** Whether SQL Server set it up for a feature of its own, such as policy management (VEY-64). */
  system: boolean;
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
  /** Whether it is the server's own housekeeping rather than an application's work (VEY-64). */
  system: boolean;
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
  /** The figures the Overview tab tells beside its weather, worked out on the server from the lists sent (VEY-68). */
  overview: DatabaseOverview;
  /** The window. */
  range: AnsweredRange;
  /** The start of the window the answer covers, as an ISO instant (ADR 0022). */
  from: string;
  /** The end of the window the answer covers: now, or the `to` asked for (ADR 0022). */
  to: string;
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

  /** The server's own databases, left out of `databases` unless System is on (VEY-66). */
  hiddenDatabases: HiddenRows;
  /** The jobs SQL Server set up itself, left out of `jobs`. */
  hiddenJobs: HiddenRows;
  /** The server's own statements, left out of `topQueries`. */
  hiddenTopQueries: HiddenRows;
  /** The catalog's tables, left out of `tables` (PostgreSQL). */
  hiddenTables: HiddenRows;
}

/** One host's metrics from `GET /api/v1/metrics/hosts/{host}`. */
export interface HostMetrics {
  /** The card numbers. */
  card: HostCard;
  /** How many of its disks fill at the pace of the last week, counted on the server (VEY-68). */
  fillingDisks: number;
  /** How many of its containers restarted within the range, counted on the server (VEY-68). */
  restartingContainers: number;
  /** The window. */
  range: AnsweredRange;
  /** The start of the window the answer covers, as an ISO instant (ADR 0022). */
  from: string;
  /** The end of the window the answer covers: now, or the `to` asked for (ADR 0022). */
  to: string;
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
  range: AnsweredRange;
  /** The start of the window the answer covers, as an ISO instant (ADR 0022). */
  from: string;
  /** The end of the window the answer covers: now, or the `to` asked for (ADR 0022). */
  to: string;
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
  /** Up to five routes, the slowest first by p95, from every route and not only the busiest (VEY-68). */
  slowestRoutes: MetricRoute[];
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
  lines: MetricChartLine[];
}

/**
 * One line of a metric's chart, with its figures over the window, read with the points (ADR 0029); its `latest` is its
 * last value in the window.
 */
export interface MetricChartLine extends ChartLine {
  /** Its average over the window; null when it has no value. */
  average: number | null;
  /** Its highest value in the window; null when it has no value. */
  highest: number | null;
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

/** Which people the Users screen lists, one chip each (VEY-67). */
export type UserShow = "everyone" | "nofactor" | "locked" | "off";

/** What the Users screen is asked for (VEY-67); the server keeps only the people that match. */
export interface UserListChoices {
  /** Text a name, email, or team must hold, in any case; empty for everyone. */
  search: string;
  /** The chip. */
  show: UserShow;
}

/** The Users screen from `GET /api/v1/users` (VEY-67): the people that match, and every figure the screen shows. */
export interface UserList {
  /** The people the search and the chip keep, ordered by email. */
  people: UserSummary[];
  /** How many of the people that match the search each chip would list. */
  counts: Record<"everyone" | "noFactor" | "locked" | "off", number>;
  /** The rings and the second-factor note, over everyone whatever the search. */
  posture: UserPosture;
  /** How many people hold each role, switched off or not. */
  roles: { admins: number; editors: number; viewers: number };
}

/** How well everyone's accounts are protected, counted by the server (VEY-67). */
export interface UserPosture {
  /** Everyone, switched off or not. */
  people: number;
  /** People who use a second factor. */
  withSecondFactor: number;
  /** People who signed in within the last 30 days. */
  recentSignIns: number;
  /** Admins who are not switched off. */
  admins: number;
  /** Of those Admins, the ones with a second factor. */
  adminsWithSecondFactor: number;
  /** People who can sign in but have no second factor. */
  missing: number;
  /** Of those, the Admins. */
  missingAdmins: number;
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
  /** Every sign-in attempt of the last 14 days, counted per day on the viewer's calendar (VEY-68). */
  signInDays: SignInDays;
}

/** A person's sign-in attempts per day, counted on the server over every attempt (VEY-68). */
export interface SignInDays {
  /** Fourteen days, the oldest first, today last. */
  days: SignInDay[];
  /** Attempts of the fourteen days that signed in. */
  signedIn: number;
  /** Attempts of the fourteen days that did not. */
  failed: number;
}

/** One day of a person's sign-in attempts. */
export interface SignInDay {
  /** The day on the viewer's calendar, `YYYY-MM-DD`. */
  day: string;
  /** Attempts that signed in, a recovery code included. */
  signedIn: number;
  /** Attempts that did not. */
  failed: number;
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
  /** How many traces read failed, counted on the server though the points hold fewer (VEY-68). */
  failed: number;
  /** How many traces read did not fail and took at least the 95th percentile (VEY-68). */
  slow: number;
}

/** A service that received requests in the window. */
export interface TraceService {
  /** The service. */
  service: string;
  /** How many of the traces read it received. */
  traces: number;
  /** The 95th percentile of their durations, in milliseconds; null without traces (ADR 0029). */
  p95Ms: number | null;
  /** How many of them failed (ADR 0029). */
  failed: number;
}

/** The figures of the traces of a window under the list's filters, from `GET /api/v1/traces/summary` (ADR 0029). */
export interface TracesSummary {
  /** The traces of the window. */
  traces: number;
  /** The traces of the period of the same length before it, for the change; null when nothing was kept then. */
  previousTraces: number | null;
  /** The traces that failed. */
  failed: number;
  /** The traces that took at least the window's 95th percentile. */
  slow: number;
  /** The median duration, in milliseconds; null without traces. */
  p50Ms: number | null;
  /** The 95th percentile of the durations, in milliseconds; null without traces. */
  p95Ms: number | null;
  /** The services that sent traces in the window. */
  services: number;
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
  /** How many ancestors it has among the spans sent; 0 for the root and for a span whose parent is missing. */
  depth: number;
  /** How many of its direct children were sent. */
  children: number;
  /** True when it names a parent that has not arrived (ADR 0014). */
  missingParent: boolean;
}

/** A whole trace: every span that has arrived, by start time. */
export interface Trace {
  /** The trace ID. */
  traceId: string;
  /** The spans; with `errorsOnly`, only the failed ones and the spans above them (VEY-67). */
  spans: TraceSpan[];
  /** The figures of every span, whatever the view, worked out by the server. */
  summary: TraceSummary;
}

/** The figures of a whole trace, worked out by the server (VEY-67). */
export interface TraceSummary {
  /** When the first span started. */
  start: string;
  /** From the start of the first span to the end of the last, in milliseconds. */
  durationMs: number;
  /** How many spans have arrived. */
  spans: number;
  /** How many of them ended in an error. */
  failed: number;
  /** How many name a parent that has not arrived. */
  missingParents: number;
  /** The services of those spans, by name. */
  missingParentServices: string[];
  /** The spans per service, most first, then by name. */
  services: TraceShare[];
  /** The IDs of the spans on the critical path. */
  criticalPath: string[];
  /** The ID of the failed span that most likely caused the failure, or null when nothing failed. */
  cause: string | null;
}

/** The sections of the Settings screen, in the order it shows them (ADR 0017). */
export type SettingSection = "Data" | "Appearance" | "Rules" | "Email" | "Security" | "Ingestion" | "Advanced";

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
  /** How many alert rules there are, counted on the server (VEY-68). */
  rules: number;
  /** How many of them are switched on now (VEY-68). */
  rulesOn: number;
}

/** How many monitored servers and agents there are and how they stand, counted on the server (VEY-68). */
export interface AgentsSummary {
  /** The SQL Servers agents read. */
  sqlServers: number;
  /** The PostgreSQL servers agents read. */
  postgresServers: number;
  /** The servers whose monitoring fails and needs a look. */
  needLook: number;
  /** The active agents. */
  agents: number;
  /** Those online now. */
  agentsOnline: number;
}

/** What the Settings screen is asked to list (VEY-67). */
export interface SettingsListingChoices {
  /** Words a setting's label, card, help, or key must all hold; empty lists the section. */
  search: string;
  /** The section, such as `Data`. */
  section: string;
  /** The view: the Simple view keeps a section to its basics. */
  view: "simple" | "advanced";
}

/** What the Settings screen lists, from `GET /api/v1/settings/listing` (VEY-67). */
export interface SettingsListing {
  /** The keys of the settings to show, in screen order. */
  keys: string[];
  /** The sections of the list, by name. */
  sections: string[];
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
  /** The named instance (`HOST\INSTANCE`, ADR 0028), read through the SQL Server Browser; null for the port. */
  instance?: string | null;
  /** How the agent signs in (ADR 0028); SQL authentication when the server did not say. */
  authentication?: SqlServerAuthentication;
  /** Each module's switch and interval (ADR 0028). */
  modules?: IntegrationModule[];
  /** The integration template's version it is read with, such as `0.2.0` (ADR 0028). */
  version?: string;
}

/**
 * How the agent signs in to a SQL Server (ADR 0028): a SQL login and password, a domain login and password over NTLM
 * from any agent, or the agent's own service account, only on a Windows agent.
 */
export type SqlServerAuthentication = "sql" | "ntlm" | "integrated";

/** One module of an integration as a server stores it (ADR 0028): whether it is read and how often. */
export interface IntegrationModule {
  /** The module, such as `core`, `activity`, `queries`, `storage`, `backups`, `jobs`, `events`. */
  id: string;
  /** Whether it is read; Core always is. */
  enabled: boolean;
  /** How often it is read, in seconds, one of the module's choices. */
  intervalSeconds: number;
}

/** A module of the SQL Server template as the catalog describes it (`GET /api/v1/integrations/modules/sqlserver`). */
export interface IntegrationModuleInfo {
  /** Its ID. */
  id: string;
  /** Its name, such as "Activity and blocking". */
  name: string;
  /** What it reads, such as "Live sessions, blocking chains, deadlocks". */
  description: string;
  /** The interval a new server takes, in seconds. */
  defaultIntervalSeconds: number;
  /** The intervals it may be read at, in seconds, shortest first. */
  choices: number[];
  /** Whether it cannot be switched off, as Core. */
  required: boolean;
}

/** An integration (ADR 0028): a monitored SQL Server with its template version and modules. */
export interface IntegrationItem {
  /** The server's ID. */
  id: string;
  /** Its kind; SQL Server only for now. */
  kind: "sqlserver";
  /** Its name in Veyra, such as `sql-prod-01`. */
  name: string;
  /** The environment its telemetry is filed under. */
  environment: string;
  /** The agent that reads it. */
  agent: { id: string; name: string };
  /** The template version it is read with. */
  version: string;
  /** The current template version Veyra ships. */
  latestVersion: string;
  /** How its monitoring stands, as on the Settings screen. */
  state: SqlServerState;
  /** Each module's switch and interval. */
  modules: IntegrationModule[];
  /** When it was added, in ISO 8601. */
  createdAt: string;
  /** When it was last changed, in ISO 8601. */
  changedAt: string;
}

/** The integrations (`GET /api/v1/integrations`). */
export interface IntegrationList {
  /** Every integration, as the server orders them. */
  items: IntegrationItem[];
}

/** The connection a probe tries, unsaved (ADR 0028), or a saved server's by its ID alone for Validate now. */
export interface ProbeInput {
  /** The agent that runs the probe. */
  agentId?: string | null;
  /** A saved server to probe with its stored connection; null for an unsaved one. */
  serverId?: string | null;
  /** The kind of SQL Server. */
  platform?: SqlServerPlatform;
  /** The host name or IP address. */
  address?: string;
  /** The TCP port, for a default instance. */
  port?: number | null;
  /** The named instance, or null for the port. */
  instance?: string | null;
  /** How the agent signs in. */
  authentication?: SqlServerAuthentication;
  /** The login, the domain login, or the service account. */
  login?: string;
  /** The password; null for integrated authentication. */
  password?: string | null;
  /** How the connection is encrypted. */
  encryption?: SqlServerEncryption;
  /** The database, for an Azure SQL Database. */
  database?: string | null;
}

/** Where a probe stands. */
export type ProbeState = "pending" | "succeeded" | "failed";

/** Why a probe failed (ADR 0028). */
export type ProbeFailure =
  "no-agent" | "agent-offline" | "unreachable" | "login-failed" | "certificate" | "timed-out" | "other";

/** What a probe read of the server. */
export interface ProbeResult {
  /** The version number, such as `16.0.4135.4`. */
  version: string;
  /** The cumulative update, such as `CU14`, or null. */
  updateLevel: string | null;
  /** The edition, such as `Enterprise Edition`. */
  edition: string;
  /** The platform it runs on, such as `Windows` or `Linux`. */
  platform: string;
  /** How many databases it has. */
  databases: number;
  /** Their names. */
  databaseNames: string[];
  /** The permissions the login lacks; empty when none. */
  missingPermissions: string[];
}

/** A probe through the agent (`GET /api/v1/integrations/probes/{id}`, ADR 0028). */
export interface Probe {
  /** Its ID. */
  id: string;
  /** Where it stands. */
  state: ProbeState;
  /** When it started, in ISO 8601. */
  startedAt: string;
  /** When it ended, or null while pending. */
  finishedAt: string | null;
  /** Why it failed, or null. */
  error: { reason: ProbeFailure; message: string } | null;
  /** What it read, or null unless it succeeded. */
  result: ProbeResult | null;
}

/** How the agent's remote configuration stands. */
export type CollectorConfigState = "applied" | "applying" | "failed" | "unknown";

/** One entry of an integration's monitoring history. */
export interface IntegrationHistoryItem {
  /** When, in ISO 8601. */
  at: string;
  /** Who, or null for Veyra itself. */
  by: string | null;
  /** What changed, such as `version`, `modules`, `created`. */
  kind: string;
  /** The value before, such as `0.1.0`, or null. */
  from: string | null;
  /** The value after, or null. */
  to: string | null;
}

/** One integration's package page (`GET /api/v1/integrations/sqlserver/{id}`, ADR 0028). */
export interface IntegrationDetails {
  /** The integration. */
  integration: IntegrationItem;
  /** The template and the login. */
  package: {
    /** The package's name, such as `veyra-sqlserver-integration`. */
    name: string;
    /** The version the server is read with. */
    version: string;
    /** The current version. */
    latestVersion: string;
    /** The version before the current one, to roll back to; null when Veyra ships only one. */
    previousVersion: string | null;
    /** When the version was set, in ISO 8601. */
    setAt: string;
    /** Who set it, or null for Veyra itself. */
    setBy: string | null;
    /** The agent's Collector version, once it said. */
    collectorVersion: string | null;
    /** The Collector versions the template states it works with, such as `otelcol-contrib ≥ 0.140.0`. */
    collectorCompatibility: string;
    /** The least SQL Server version the template supports, such as `SQL Server 2016`. */
    minimumSqlServer: string;
    /** The login the agent signs in with. */
    login: string;
    /** How it signs in. */
    authentication: SqlServerAuthentication;
    /** The permissions the login lacks; empty when none. */
    loginLacks: string[];
  };
  /** The agent's Collector, from OpAMP. */
  collector: {
    agentId: string;
    agentName: string;
    /** Whether OpAMP reports it healthy. */
    healthy: boolean;
    /** When the Collector started, or null. */
    startedAt: string | null;
    /** How long it has run, or null. */
    uptimeSeconds: number | null;
    /** When it last reported, or null. */
    lastSeenAt: string | null;
    /** The remote configuration's status. */
    configState: CollectorConfigState;
    /** Why the configuration failed, or null. */
    configError: string | null;
  };
  /** The configuration validation: the last probe of the saved server. */
  validation: { lastProbe: Probe | null };
  /** Each module with what it collected over the window. */
  modules: {
    id: string;
    name: string;
    enabled: boolean;
    intervalSeconds: number;
    /** Metric series it wrote in the window. */
    series: number;
    /** Points it wrote in the window. */
    points: number;
    /** Points over the window, oldest first. */
    line: (number | null)[];
  }[];
  /** The integration's reads with the time SQL Server spends per run, from the statement statistics. */
  checks: { name: string; module: string; secondsPerRun: number | null; runs: number; ok: boolean }[];
  /** What the installation still lacks. */
  nextSteps: { id: string; text: string; done: boolean; link: string | null }[];
  /** The monitoring history, newest first. */
  history: IntegrationHistoryItem[];
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
  /** The named instance, or null to connect to the port (ADR 0028); the stored one when left out. */
  instance?: string | null;
  /** How the agent signs in (ADR 0028); the stored one, or SQL authentication, when left out. */
  authentication?: SqlServerAuthentication;
  /** Each module's switch and interval (ADR 0028); the stored ones, or the defaults, when left out. */
  modules?: IntegrationModule[];
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
  /** The session that runs the longest request, or null; the whole server's, so a filter can leave it out (VEY-66). */
  longestRequestSession: number | null;
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
  /** The sessions with a request running now, the longest first, picked on the server (VEY-68). */
  running: LiveSession[];
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
  /**
   * The sessions of the listed chains that do not meet the filters, so a chain still names who blocks whom (VEY-66);
   * empty without filters.
   */
  chained: LiveSession[];
}

/** What a list left out because System is off (VEY-66): the server filters, and the screen only says so. */
export interface HiddenRows {
  /** How many rows were left out. */
  count: number;
  /** The names among them an alert can be about, such as `msdb` or a job's name, at most 20. */
  names: string[];
}

/** What the server ranks a Queries tab's statements by before it keeps the top 100 (VEY-66). */
export type QuerySort = "totalTime" | "cpu" | "reads" | "runs" | "timePerRun";

/** The Live tab's filters, sent to the server, which lists only the sessions with every value given (VEY-66). */
export interface LiveFilterValues {
  /** The database a session uses. */
  database?: string;
  /** The program that opened it. */
  program?: string;
  /** The machine it comes from. */
  host?: string;
  /** Its login. */
  login?: string;
  /** The wait type its request waits on. */
  wait?: string;
}

/** A SQL Server's Jobs tab from `GET .../jobs` (ADR 0020, area 10). */
export interface AgentJobsView {
  /** The jobs, those that need a look first. */
  jobs: AgentJobView[];
  /** The jobs SQL Server set up itself, left out unless System is on. */
  hiddenJobs: HiddenRows;
  /** How many enabled jobs are listed before the Simple view keeps those that need a look (VEY-67). */
  enabled: number;
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
  /** Whether it is the server's own housekeeping rather than an application's work (VEY-64). */
  system: boolean;
}

/** A SQL Server's Queries tab. */
export interface DatabaseQueries {
  /** The range: the preset asked for, or `custom`. */
  range: string;
  /** The start of the window the answer covers, as an ISO instant (ADR 0022). */
  from: string;
  /** The end of the window the answer covers: now, or the `to` asked for (ADR 0022). */
  to: string;
  /** The databases that do not keep Query Store, picked on the server (VEY-68). */
  queryStoreOff: QueryStoreState[];
  /** The statements by total time; Veyra's agent left out. */
  statements: QueryRow[];
  /** Statements of Veyra's agent. */
  agentStatements: number;
  /** Their time. */
  agentSeconds: number;
  /** Each user database and whether it keeps Query Store. */
  queryStore: QueryStoreState[];
  /** The server's own statements, left out unless System is on (VEY-66). */
  hiddenStatements: HiddenRows;
  /** The busiest statement over the range, which the Simple view names when none shows a problem (VEY-67). */
  busiest: QueryRow | null;
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
  /** How many reads saw any program run it, added up on the server (VEY-68). */
  callerSamples: number;
  /** Its database, or null. */
  database: string | null;
  /** What it does, in words. */
  name: string;
  /** The statement. */
  text: string;
  /** The range: the preset asked for, or `custom`. */
  range: string;
  /** The start of the window the answer covers, as an ISO instant (ADR 0022). */
  from: string;
  /** The end of the window the answer covers: now, or the `to` asked for (ADR 0022). */
  to: string;
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
  /** The range: the preset asked for, or `custom`. */
  range: string;
  /** The start of the window the answer covers, as an ISO instant (ADR 0022). */
  from: string;
  /** The end of the window the answer covers: now, or the `to` asked for (ADR 0022). */
  to: string;
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
  /** Whether the engine keeps the database for itself, such as master or msdb (VEY-64). */
  system: boolean;
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

/** The figures a database server's Overview tab tells beside its weather (VEY-68). */
export interface DatabaseOverview {
  /** The wait that took the most time over the range, with its share from 0 to 1; null when nothing waited. */
  topWait: { label: string; share: number } | null;
  /** The databases, tempdb aside, without a full backup within 26 hours. */
  lateBackups: string[];
  /** The jobs whose last run failed. */
  failedJobs: AgentJob[];
  /** The database oldest to transaction ID wraparound (PostgreSQL); null where the engine has none. */
  oldestWraparound: DatabasePart | null;
}

/** A SQL Server's databases and volumes in full. */
export interface StorageView {
  /** Its databases. */
  databases: DatabaseStorage[];
  /** Its volumes. */
  volumes: DatabaseVolume[];
  /** Server-wide findings. */
  findings: PlanWarning[];
  /** The server's own databases, left out unless System is on (VEY-66). */
  hiddenDatabases: HiddenRows;
  /** The findings on those databases, left out with them. */
  hiddenFindings: HiddenRows;
  /**
   * The server's findings and the listed databases' in one list, the warnings first, each with its database, for the
   * Needs doing panel (VEY-68).
   */
  needsDoing: StorageFinding[];
}

/** One finding of the Storage tab's Needs doing list (VEY-68). */
export interface StorageFinding {
  /** What Veyra found, with the statement to run. */
  finding: PlanWarning;
  /** The database it is about, or null for the server's own finding. */
  database: string | null;
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
  /** Whether it is in a database the engine keeps for itself (VEY-64). */
  system: boolean;
}

/** A SQL Server's index and statistics advice. */
export interface IndexAdvice {
  /** The findings, with the statements to run. */
  findings: PlanWarning[];
  /** The largest indexes. */
  indexes: IndexInfo[];
  /** The indexes of the server's own databases, left out with their findings unless System is on (VEY-66). */
  hiddenIndexes: HiddenRows;
}

/** A wait type over the range, explained (ADR 0020, area 2). */
export interface WaitTypeTotal {
  /** The type explained. */
  term: WaitTerm;
  /** Seconds waited on it over the range. */
  seconds: number;
  /** Whether it is a background task's idle waiting, which says nothing about the load (VEY-64). */
  system: boolean;
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
  /** The idle waits of the server's background tasks, left out unless System is on (VEY-66). */
  hiddenWaits: HiddenRows;
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
  /** Whether SQL Server set it up for a feature of its own, such as policy management (VEY-64). */
  system: boolean;
  /** Whether it needs a look: it failed, is late, or runs at least twice its usual time (VEY-67). */
  needsLook: boolean;
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
  /** What is not well, in words, worked out on the server (VEY-68). */
  trouble: string[];
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

/** One thing a search found (ADR 0022). */
export interface SearchItem {
  /** What it is. */
  kind: "service" | "host" | "container" | "server" | "database" | "operation";
  /** Its name. */
  name: string;
  /** Its health from the alert rules, or null when it has none. */
  health?: BlockHealth | null;
  /** A short figure about it, worded by the server. */
  detail: string;
  /** For a database server or a database: its engine, as `db.system.name` spells it. */
  engine?: string;
  /** For a database: its server. For a server: the same as its name. */
  server?: string;
  /** For an operation: the service it belongs to. */
  service?: string;
  /** For an operation: how many traces it has in the window. */
  count?: number;
}

/** One group of a search's answer. */
export interface SearchGroup {
  /** Up to five matches, best first, as the server ranks them. */
  items: SearchItem[];
  /** Whether the group ran out of time and holds only what it found by then. */
  partial: boolean;
}

/** The answer of `GET /api/v1/search`: every signal searched at once, each group ranked by the server. */
export interface SearchResults {
  /** The words searched. */
  query: string;
  /** Services. */
  services: SearchGroup;
  /** Hosts and containers. */
  hosts: SearchGroup;
  /** Database servers and databases. */
  databases: SearchGroup;
  /** Trace operations. */
  traces: SearchGroup;
  /** How many log records hold the words in the window. */
  logs: { count: number; partial: boolean };
}

/** A name with a share, such as the busiest host and its CPU. */
export interface NamedRatio {
  /** The name. */
  name: string;
  /** The share, from 0 to 1. */
  ratio: number;
}

/** What every resource list asks for (ADR 0022): the shared window, the environment, the filters, and the page. */
export interface ResourceQuery {
  /** The window's start, as ISO. */
  from: string;
  /** The window's end, as ISO. */
  to: string;
  /** The chosen environment, or null for all. */
  environment: string | null;
  /** The filters, by their parameter name; empty values are left out. */
  filters: Record<string, string>;
  /** Where the next page starts, or null for the first. */
  cursor: string | null;
}

/** One page of a resource list: the header for the whole filtered set, the items worst first, and where to go on. */
export interface ResourcePage<THeader, TItem> {
  /** Figures for the whole filtered set, not only this page. */
  header: THeader;
  /** This page's items, worst first unless the person sorted by name. */
  items: TItem[];
  /** How many items the filters keep. */
  total: number;
  /** Where the next page starts, or null on the last. */
  next: string | null;
}

/** The figures of the Services screen's header. Every change is against the previous period of the same length. */
export interface ServicesHeader {
  /** Every service the filters keep. */
  total: number;
  /** Those without an open alert. */
  healthy: number;
  /** Those with an open warning. */
  warning: number;
  /** Those with an open caution and no warning. */
  caution: number;
  /** Those that sent nothing for the silence setting. */
  silent: number;
  /** Requests in the window, or null for services without HTTP metrics. */
  requests: number | null;
  /** The change of requests, as a ratio, or null without a previous period. */
  requestsChange: number | null;
  /** The share of requests answered with a server error in the window, or null without HTTP metrics. */
  errorRatio: number | null;
  /** The change of the error ratio, in percentage points as a ratio. */
  errorRatioChange: number | null;
  /** 95 of 100 requests were answered faster than this, or null without HTTP metrics. */
  p95Seconds: number | null;
  /** The change of the p95, as a ratio, or null without a previous period. */
  p95Change: number | null;
  /** Running copies of the services, from `service.instance.id`. */
  instances: number;
  /** The hosts they run on. */
  hosts: number;
}

/** One service of the Services screen. */
export interface ServiceRow {
  /** The service, by `service.name`. */
  name: string;
  /** Its environment, or null when it sends none. */
  environment: string | null;
  /** Its health from the alert rules. */
  health: BlockHealth;
  /** Whether nothing arrived for the silence setting (2 hours by default). */
  silent: boolean;
  /** Requests per minute in the window, or null without HTTP metrics. */
  requestsPerMinute: number | null;
  /** Its p95 latency, or null without HTTP metrics. */
  p95Seconds: number | null;
  /** Its share of server errors, or null. */
  errorRatio: number | null;
  /** Its running copies. */
  instances: number;
  /** The hosts it runs on, by `host.name`. */
  hosts: string[];
  /** When it last sent anything, as ISO, or null. */
  lastSeenAt: string | null;
  /** Requests per minute in each 24th of the window, for the row's line; null where nothing is known. */
  requests: (number | null)[];
}

/** The figures of the Hosts & containers screen's header. */
export interface HostsHeader {
  /** Every host and container seen while its metrics are kept. */
  known: number;
  /** Those that reported lately. */
  online: number;
  /** Containers among them. */
  containers: number;
  /** The average CPU use, from 0 to 1, or null. */
  cpuAverage: number | null;
  /** The busiest one, or null. */
  cpuBusiest: NamedRatio | null;
  /** The average memory use, from 0 to 1, or null. */
  memoryAverage: number | null;
  /** The fullest one, or null. */
  memoryFullest: NamedRatio | null;
  /** Disks that fill within 30 days. */
  disksFilling: number;
  /** The disk that fills first, or null. */
  disksFillingFirst: { host: string; mount: string; hoursLeft: number } | null;
}

/** One host or container of the Hosts & containers screen. */
export interface HostRow {
  /** Its name, by `host.name` or the container name. */
  name: string;
  /** Whether it is a host or a container. */
  kind: "host" | "container";
  /** Its operating system, for a host, or null. */
  os: string | null;
  /** For a container: the host it runs on. */
  parent: string | null;
  /** Its health from the alert rules. */
  health: BlockHealth;
  /** Whether it reported nothing lately. */
  silent: boolean;
  /** Its CPU use, from 0 to 1, or null. */
  cpuRatio: number | null;
  /** Its memory use, from 0 to 1, or null. */
  memoryRatio: number | null;
  /** How full its fullest disk is, or null. */
  fullestDiskRatio: number | null;
  /** What runs on it: services, or database servers. */
  runs: string[];
  /** How many things run on it. */
  runsCount: number;
  /** When it last reported, as ISO, or null. */
  lastSeenAt: string | null;
}

/** The figures of the Databases screen's header. */
export interface DatabasesHeader {
  /** Every database server Veyra watches. */
  servers: number;
  /** Those that sent nothing lately. */
  silent: number;
  /** Their databases, with or without the system ones as the filter says. */
  databases: number;
  /** The size of every database together, or null. */
  dataBytes: number | null;
  /** How much they grow per day together, or null. */
  growthBytes: number | null;
  /** Databases whose last full backup is late. */
  lateBackups: number;
  /** The first late one, or null. */
  lateBackupFirst: { server: string; database: string } | null;
  /** Blocking chains now, across the servers. */
  blockingChains: number;
  /** The server with the most, or null. */
  blockingServer: string | null;
}

/** One database server of the Databases screen. */
export interface DatabaseServerRow {
  /** Its engine, as `db.system.name` spells it. */
  engine: string;
  /** Its name in Veyra. */
  server: string;
  /** Its version in words, or null. */
  version: string | null;
  /** Its health from the alert rules. */
  health: BlockHealth;
  /** Whether it sent nothing lately. */
  silent: boolean;
  /** Its databases. */
  databases: number;
  /** Their size together, or null. */
  sizeBytes: number | null;
  /** Their growth per day, or null. */
  growthBytesPerDay: number | null;
  /** The age of its oldest last full backup, or null. */
  lastFullBackupAgeSeconds: number | null;
  /** Its databases with a late backup. */
  lateBackups: number | null;
  /** Its blocking chains now. */
  blockingChains: number | null;
  /** Its CPU use, from 0 to 1, or null. */
  cpuRatio: number | null;
}

/** A figure and its change against the period before (ADR 0022), as the server works it out. */
export interface FigureChange {
  /** The change: relative for a count, in ratio units for a ratio; null without a previous period. */
  change: number | null;
}

/** The header figures of the Overview (ADR 0023). */
export interface OverviewFigures {
  /** Services by health; silent ones sent nothing lately. */
  services: { healthy: number; total: number; warning: number; caution: number; silent: number };
  /** Hosts that reported lately, of every host Veyra knows. */
  hosts: { online: number; known: number };
  /** Database servers Veyra watches and their databases, system ones left out. */
  databases: { servers: number; databases: number };
  /** HTTP requests in the window; null without HTTP metrics. */
  requests: FigureChange & { count: number | null };
  /** The share of requests answered with a server error; null without HTTP metrics. */
  errorRatio: FigureChange & { value: number | null };
  /** Open alerts by severity. */
  openAlerts: { warning: number; caution: number };
  /** SQL Server Agent job runs in the window: the share that succeeded, null without runs. */
  jobs: { succeededRatio: number | null; runs: number; failed: number };
  /** The share of the last days without an open warning on any service; null without services. */
  availability: FigureChange & { ratio: number | null; days: number };
}

/** A series of the Overview: equal steps across the window. */
export interface OverviewSeries<TPoint> {
  /** The width of each step, in seconds. */
  stepSeconds: number;
  /** The steps, oldest first. */
  points: TPoint[];
}

/** One step of the request activity. */
export interface ActivityPoint {
  /** The step's start, as ISO. */
  at: string;
  /** Requests in the step, or null where unknown. */
  requests: number | null;
  /** Server errors in the step, or null where unknown. */
  errors: number | null;
}

/** One step of the telemetry that arrived. */
export interface IngestionPoint {
  /** The step's start, as ISO. */
  at: string;
  /** Log records. */
  logs: number;
  /** Spans. */
  spans: number;
  /** Metric points. */
  points: number;
}

/** One step of the hosts' resources. */
export interface HostTrendPoint {
  /** The step's start, as ISO. */
  at: string;
  /** The average CPU use across hosts, from 0 to 1, or null. */
  cpu: number | null;
  /** The average memory use across hosts, or null. */
  memory: number | null;
  /** The fullest disk across hosts, or null. */
  disk: number | null;
}

/** The latest run of a job, such as a SQL Server Agent job. */
export interface PipelineRun {
  /** The job's name. */
  name: string;
  /** The server it runs on. */
  server: string;
  /** The server's engine; null for a job recognised from traces, which runs in a service. */
  engine: string | null;
  /** When it last ran, as ISO, or null. */
  lastRunAt: string | null;
  /** How long the last run took, or null. */
  durationSeconds: number | null;
  /** How the last run ended, or that it runs now. */
  status: "Succeeded" | "Failed" | "Running";
  /** Its runs in the window. */
  runs: number;
  /** Those that failed. */
  failedRuns: number;
  /** The job's ID, for its own screen (ADR 0027); missing from servers older than job runs. */
  jobId?: string;
}

/** An agent as the Overview shows it. */
export interface CollectorHealth {
  /** Its name. */
  name: string;
  /** Its state, as the Agents page says it. */
  state: AgentState;
  /** When it last reported, as ISO, or null. */
  lastSeenAt: string | null;
  /** The Collector version it runs, or null. */
  version: string | null;
  /** Its exporter queue, or null until agents report it (ADR 0023). */
  queue: number | null;
}

/** One database among every server's databases. */
export interface DatabaseAcross {
  /** The database. */
  name: string;
  /** Its server. */
  server: string;
  /** The server's engine. */
  engine: string;
  /** Its state, such as Online, or null where the engine does not report it. */
  state: string | null;
  /** Its size, or null. */
  sizeBytes: number | null;
  /** Its growth per day, or null. */
  growthBytesPerDay: number | null;
  /** The age of its last full backup, or null. */
  lastFullBackupAgeSeconds: number | null;
  /** Whether that backup is late. */
  late: boolean;
  /** Its connections, where the engine reports them per database, or null. */
  connections: number | null;
}

/** The answer of `GET /api/v1/overview` (ADR 0023): the whole environment on one screen. */
export interface Overview {
  /** The window's start. */
  from: string;
  /** The window's end. */
  to: string;
  /** The header figures. */
  figures: OverviewFigures;
  /** Requests and server errors across services. */
  activity: OverviewSeries<ActivityPoint>;
  /** Telemetry that arrived, by signal. */
  ingestion: OverviewSeries<IngestionPoint>;
  /** The hosts' resources. */
  hostTrends: OverviewSeries<HostTrendPoint>;
  /** The latest runs of the jobs, failed ones first. */
  pipelines: PipelineRun[];
  /** The agents; null for anyone but an Admin. */
  collectors: CollectorHealth[] | null;
  /** Every server's databases, worst first, as one page. */
  databases: { items: DatabaseAcross[]; total: number };
}

/** One figure of a SQL Server's overview (ADR 0025): its value now, a line over the range, and its change. */
export interface ServerFigure {
  /** The value now; null when the server did not send it. */
  value: number | null;
  /** The value over the range, one point per step. */
  line: (number | null)[];
  /** The change against the period before: relative for an amount, in ratio units for a ratio; null without one. */
  change: number | null;
}

/** The machine a SQL Server runs on, and the instance (ADR 0025). */
export interface ServerMachine {
  /** The SQL Server version, such as "16.0.4135.4". */
  version: string | null;
  /** The cumulative update, such as "CU14". */
  updateLevel: string | null;
  /** The edition, such as "Enterprise Edition". */
  edition: string | null;
  /** The operating system, such as "Windows Server 2022". */
  osName: string | null;
  /** Its release, such as "21H2". */
  osRelease: string | null;
  /** The logical CPUs SQL Server sees. */
  cpus: number | null;
  /** The machine's physical memory in bytes. */
  memoryBytes: number | null;
  /** When the instance started. */
  startedAt: string | null;
  /** How long it has run, in seconds. */
  uptimeSeconds: number | null;
  /** Its databases, system ones left out unless asked for. */
  databases: number;
  /** The platform, such as "box", "managed-instance", or "azure-sql-database". */
  platform: string | null;
}

/** A blocking chain from the latest live read (ADR 0025). */
export interface ServerBlockingChain {
  /** The session at the head of the chain. */
  headSessionId: number;
  /** Its database. */
  database: string | null;
  /** Its login. */
  login: string | null;
  /** The machine it came from. */
  host: string | null;
  /** What it waits for, when it waits itself. */
  waitType: string | null;
  /** How long the longest blocked session has waited, in seconds. */
  waitSeconds: number | null;
  /** How many sessions wait behind it, directly or not. */
  blockedCount: number;
  /** The statement it runs. */
  statement: string | null;
  /** The sessions it blocks directly. */
  blocked: { sessionId: number; waitType: string | null; waitSeconds: number | null }[];
}

/** A monitoring area of a SQL Server (ADR 0025). */
export type ServerArea =
  "activity" | "waits" | "queries" | "storage" | "backups" | "jobs" | "events" | "configuration" | "availability";

/** A monitoring area of a SQL Server and whether its data arrives (ADR 0025). */
export interface ServerCoverage {
  /** The area. */
  area: ServerArea;
  /** Whether data arrived lately. */
  delivering: boolean;
  /** When data last arrived. */
  lastSeen: string | null;
  /** The permission the monitoring login lacks for it, when that is why it is silent. */
  missingPermission: string | null;
}

/** A core counter of a SQL Server, averaged over the range (ADR 0025). */
export interface ServerCounter {
  /** Which counter. */
  name: "batchRequests" | "compilations" | "pageLife" | "waits";
  /** Its average over the range. */
  value: number | null;
  /** Its unit in words, such as "per second". */
  unit: string;
  /** The change against the period before. */
  change: number | null;
}

/** A SQL Server's overview, in one read (`GET …/overview`, ADR 0025). */
export interface ServerOverview {
  /** The window, as ISO instants. */
  from: string;
  /** Its end. */
  to: string;
  /** The machine and the instance. */
  machine: ServerMachine;
  /** The header figures. */
  figures: {
    cpu: ServerFigure;
    sqlMemory: ServerFigure;
    sessions: ServerFigure;
    blocked: ServerFigure;
    size: ServerFigure;
    lastFullBackupSeconds: ServerFigure;
    batchRequests: ServerFigure;
    volumeFree: ServerFigure & { volume: string | null };
  };
  /** CPU, memory, and waits over the range. */
  performance: {
    bucketSeconds: number;
    starts: string[];
    cpu: (number | null)[];
    sqlMemoryRatio: (number | null)[];
    waitMsPerSecond: (number | null)[];
  };
  /** The core counters, with their change. */
  counters: ServerCounter[];
  /** Reads and writes in bytes per second. */
  io: {
    readBytesPerSecond: number | null;
    writeBytesPerSecond: number | null;
    readLine: (number | null)[];
    writeLine: (number | null)[];
  };
  /** The top wait types of the range, idle ones left out. */
  waits: { totalSeconds: number; types: { type: string; category: string | null; seconds: number; share: number }[] };
  /** Sizes, growth, the largest databases, and the volumes. */
  storage: {
    totalBytes: number | null;
    growth24hBytes: number | null;
    growth7dBytes: number | null;
    growth30dBytes: number | null;
    line: (number | null)[];
    largest: { database: string; bytes: number; growth24hBytes: number | null; growth7dBytes: number | null }[];
    volumes: { name: string; sizeBytes: number; freeBytes: number; freeRatio: number; daysUntilFull: number | null }[];
  };
  /** Each database. */
  inventory: {
    database: string;
    dataBytes: number | null;
    logBytes: number | null;
    growthPerDayBytes: number | null;
    state: string | null;
    recoveryModel: string | null;
  }[];
  /** Blocking now and over the range. */
  blocking: { chains: ServerBlockingChain[]; blockedLine: (number | null)[]; deadlocks: number };
  /** Which monitoring areas deliver data. */
  coverage: ServerCoverage[];
}

/** A backup type as SQL Server records it. */
export type BackupType = "full" | "differential" | "log";

/** How many backups of one type ran on a day, and how long they took together. */
export interface BackupDayType {
  /** How many ran. */
  count: number;
  /** Their seconds together. */
  seconds: number;
}

/** A SQL Server's backups (`GET …/backups`, ADR 0025). */
export interface ServerBackups {
  /** The window. */
  from: string;
  /** Its end. */
  to: string;
  /** Whether the platform backs up by itself, as Azure SQL Database and Managed Instance do. */
  selfManaged: boolean;
  /** The backups of the window, newest first, at most 500. */
  backups: {
    database: string;
    type: BackupType;
    startedAt: string;
    finishedAt: string;
    seconds: number;
    bytes: number | null;
    compressedBytes: number | null;
    copyOnly: boolean;
    device: string | null;
  }[];
  /** Each day of the window, oldest first. */
  days: { day: string; full: BackupDayType; differential: BackupDayType; log: BackupDayType }[];
  /** The last 7 days per database. */
  grid: {
    days: string[];
    databases: { database: string; cells: { full: boolean; differential: boolean; log: number }[] }[];
  };
  /** Each database's freshness against the backup rules, worst first. */
  freshness: {
    database: string;
    fullAgeSeconds: number | null;
    diffAgeSeconds: number | null;
    logAgeSeconds: number | null;
    recoveryModel: string | null;
    state: "ok" | "caution" | "warning" | "ignored";
    policy: string;
  }[];
}

/** Where an incident stands (ADR 0026). */
export type IncidentState = "open" | "acknowledged" | "resolved";

/** An incident: related alerts grouped into one thing to work on (ADR 0026). */
export interface IncidentItem {
  /** The incident's ID. */
  id: string;
  /** Its number. */
  number: number;
  /** Its key, such as "INC-42". */
  key: string;
  /** Its title: the headline of its first or worst alert. */
  title: string;
  /** Its environment. */
  environment: string | null;
  /** The worst severity of its alerts. */
  severity: AlertSeverity;
  /** Where it stands. */
  state: IncidentState;
  /** When its first alert opened. */
  openedAt: string;
  /** When someone acknowledged it. */
  acknowledgedAt: string | null;
  /** Who acknowledged it. */
  acknowledgedBy: string | null;
  /** When its last alert resolved. */
  resolvedAt: string | null;
  /** How many alerts it holds. */
  alertCount: number;
  /** The blocks its alerts are about. */
  blocks: AlertBlock[];
}

/** One page of incidents. */
export interface IncidentPage {
  /** The incidents, open first, then newest. */
  items: IncidentItem[];
  /** Pass it as `cursor` for the next page; null on the last one. */
  next: string | null;
}

/** One moment of an incident's own timeline: its alerts' events, their notifications, and acknowledgements. */
export interface IncidentMoment {
  /** When it happened. */
  at: string;
  /** What happened. */
  kind: "opened" | "escalated" | "eased" | "resolved" | "acknowledged" | "unacknowledged";
  /** The alert it happened to. */
  alertId: string | null;
  /** That alert's rule. */
  rule: AlertRule | null;
  /** That alert's block. */
  block: AlertBlock | null;
  /** The severity after it. */
  severity: AlertSeverity | null;
  /** The measure then. */
  value: number | null;
  /** Who did it, for an acknowledgement. */
  actor: string | null;
  /** The notifications it sent. */
  deliveries: AlertDelivery[];
}

/** An incident with its alerts, figures, and timeline (`GET /api/v1/incidents/{id}`). */
export interface IncidentDetails {
  /** The incident. */
  incident: IncidentItem;
  /** Its alerts, as the alert list shows them. */
  alerts: AlertListItem[];
  /** Its figures. */
  figures: {
    firstDetectedAt: string;
    durationSeconds: number;
    affectedBlocks: number;
    leading: {
      alertId: string;
      rule: AlertRule;
      value: number;
      threshold: number;
      peak: number;
      peakAt: string | null;
    };
  };
  /** Its own timeline, oldest first. */
  timeline: IncidentMoment[];
  /** The runbook links of its rules that have one. */
  runbooks: { rule: AlertRule; url: string }[];
}

/** One step of an incident's execution path. */
export interface IncidentStep {
  /** The step's name: a span's or a job step's. */
  name: string;
  /** The service that ran it. */
  service: string | null;
  /** How long it took, in milliseconds. */
  durationMs: number | null;
  /** How it ended. */
  status: "ok" | "error" | "unset";
  /** Its status message. */
  detail: string | null;
}

/** A moment from another signal, on an incident's timeline. */
export interface IncidentSignal {
  /** When it happened. */
  at: string;
  /** What kind of moment. */
  kind: "job-failed" | "config-change" | "deadlock" | "first-error";
  /** The block it happened on. */
  block: AlertBlock;
  /** What happened, in words. */
  title: string;
  /** More about it. */
  detail: string | null;
}

/** A chart of an affected block. */
export interface IncidentChart {
  /** Its title. */
  title: string;
  /** The block it is about. */
  block: AlertBlock;
  /** The unit of its values. */
  unit: "ratio" | "ms" | "count" | "bytes" | "percent";
  /** Its lines. */
  lines: ChartLine[];
}

/**
 * What surrounds an alert (`GET /api/v1/alerts/{id}/context`, ADR 0029): the blocks it touches with their state now,
 * and the warnings and worse they logged from an hour before it opened until it closed or now.
 */
export interface AlertContext {
  /** The window looked at. */
  from: string;
  /** Its end. */
  to: string;
  /** Warnings and worse from those blocks, newest first, at most 50, with the total. */
  logs: IncidentContext["logs"];
  /** The alert's block and the blocks related to it, each with its state now. */
  components: IncidentContext["components"];
}

/** What surrounds an incident (`GET /api/v1/incidents/{id}/context`, ADR 0026). */
export interface IncidentContext {
  /** The window looked at. */
  from: string;
  /** Its end. */
  to: string;
  /** The path that failed, from a trace or a job run. */
  executionPath: {
    source: "trace" | "job";
    traceId: string | null;
    jobName: string | null;
    steps: IncidentStep[];
  } | null;
  /** The exception behind it. */
  exception: {
    type: string | null;
    message: string;
    stackTrace: string | null;
    service: string | null;
    traceId: string | null;
    source: "trace" | "logs";
  } | null;
  /** Moments from other signals. */
  timeline: IncidentSignal[];
  /** Charts of the affected blocks, at most 4. */
  charts: IncidentChart[];
  /** Warnings and worse from the affected blocks. */
  logs: { items: LogEntry[]; query: string; total: number | null };
  /** The affected blocks and those related to them. */
  components: {
    block: AlertBlock;
    health: "ok" | "caution" | "warning" | "silent";
    inIncident: boolean;
    relation: "affected" | "host" | "runs-on-host";
  }[];
}

/** Where a job's runs come from (ADR 0027): root spans carrying a job name, or SQL Server Agent's history. */
export type JobSource = "trace" | "sqlagent";

/** How a job run ended, or that it runs now. */
export type JobRunStatus = "succeeded" | "failed" | "running";

/** Which jobs the list keeps: those whose latest run failed, those running now, or every job. */
export type JobStatusFilter = "failing" | "running" | "all";

/** The latest run of a job. */
export interface JobLastRun {
  /** When it started, as ISO. */
  startedAt: string;
  /** How long it took, or null while it runs or when unknown. */
  durationSeconds: number | null;
  /** How it ended, or that it runs now. */
  status: JobRunStatus;
}

/** One job of `GET /api/v1/jobs` (ADR 0027): its source, block, environment, and name make it one job. */
export interface JobListItem {
  /** Its opaque, URL-safe ID, for `GET /api/v1/jobs/{id}`. */
  id: string;
  /** Its name, such as `CustomerSync` or `Nightly backup`. */
  name: string;
  /** Where its runs come from. */
  source: JobSource;
  /** The service or database server it runs on. */
  block: AlertBlock;
  /** Its environment, or null when the telemetry names none. */
  environment: string | null;
  /** Its latest run, or null when none is kept. */
  lastRun: JobLastRun | null;
  /** Its runs in the window. */
  runs: number;
  /** Those that failed. */
  failed: number;
  /** The share of the window's finished runs that succeeded, from 0 to 1; null without one. */
  successRatio: number | null;
  /** How long a run usually takes: the median of the window, or null. */
  usualSeconds: number | null;
  /** The statuses of the latest 20 runs, oldest first. */
  recent: JobRunStatus[];
  /** Whether a run is going on now. */
  running: boolean;
}

/** The figures over the jobs the list keeps. */
export interface JobListFigures {
  /** The jobs. */
  jobs: number;
  /** Their runs in the window. */
  runs: number;
  /** Those that failed. */
  failed: number;
  /** The share of finished runs that succeeded, from 0 to 1; null without one. */
  successRatio: number | null;
  /** Runs going on now. */
  running: number;
}

/** The answer of `GET /api/v1/jobs` (ADR 0027): failing jobs first, then by name. */
export interface JobList {
  /** The window's start. */
  from: string;
  /** Its end. */
  to: string;
  /** The figures over the jobs listed. */
  figures: JobListFigures;
  /** The jobs. */
  items: JobListItem[];
}

/** What the jobs list is asked for; the server filters, counts, and orders (ADR 0027). */
export interface JobQuery {
  /** The window's start, as ISO. */
  from: string;
  /** Its end. */
  to: string;
  /** The environment of the top bar, or null for all. */
  environment: string | null;
  /** One source only, or null for both. */
  source: JobSource | null;
  /** Words the job's name or block must contain; empty for any. */
  q: string;
  /** Which jobs. */
  status: JobStatusFilter;
}

/** One step of a job run: a direct child of the run's root span, or a SQL Server Agent job step. */
export interface JobStep {
  /** Its name. */
  name: string;
  /** How long it took, or null. */
  durationSeconds: number | null;
  /** How it ended; unknown when the telemetry did not say. */
  status: JobRunStatus | "unknown";
  /** What it reported, such as the error, or null. */
  message: string | null;
}

/** One run of a job, as the job's screen lists it. */
export interface JobRun {
  /** The run's ID: the pipeline run ID, the trace ID, or SQL Server Agent's instance ID. */
  runId: string;
  /** When it started, as ISO. */
  startedAt: string;
  /** How long it took, or null while it runs. */
  durationSeconds: number | null;
  /** How it ended, or that it runs now. */
  status: JobRunStatus;
  /** What it reported, such as the error of a failed run, or null. */
  message: string | null;
  /** The trace of a traced run, or null. */
  traceId: string | null;
  /** Whether the trace's spans are still kept (7 days, ADR 0014); the run itself is kept 30 days. */
  traceKept: boolean;
  /** The records it processed, or null when the run did not say. */
  recordsProcessed: number | null;
  /** The records it expected to process, or null. */
  recordsTotal: number | null;
  /** How often it retried, or null. */
  retries: number | null;
  /** Its steps, in the order they ran. */
  steps: JobStep[];
}

/** The figures of one job over the window. */
export interface JobFigures {
  /** Its runs. */
  runs: number;
  /** Those that failed. */
  failed: number;
  /** The share of finished runs that succeeded, from 0 to 1; null without one. */
  successRatio: number | null;
  /** The median duration, or null. */
  medianSeconds: number | null;
  /** The 95th percentile duration, or null. */
  p95Seconds: number | null;
  /** The records its runs processed, or null when none said. */
  recordsProcessed: number | null;
}

/** One run's duration, for the chart. */
export interface JobDuration {
  /** When it started, as ISO. */
  at: string;
  /** How long it took, or null while it runs. */
  seconds: number | null;
  /** How it ended, or that it runs now. */
  status: JobRunStatus;
}

/** The answer of `GET /api/v1/jobs/{id}` (ADR 0027): one job over the window. */
export interface JobDetails {
  /** The window's start. */
  from: string;
  /** Its end. */
  to: string;
  /** The job, as the list shows it. */
  job: JobListItem;
  /** Its figures. */
  figures: JobFigures;
  /** Each run's duration, oldest first. */
  durations: JobDuration[];
  /** Its runs, newest first. */
  runs: JobRun[];
}

/** The Veyra that answers (VEY-82). */
export interface About {
  /** Its release, such as `0.4.1`. */
  version: string;
  /** Which build of the app it serves; an open page that loaded another one offers to reload. */
  build: string;
}

/** One part of a release's notes, such as what was added. */
export interface ReleaseSection {
  /** Its heading: Added, Changed, Fixed, or Removed. */
  title: string;
  /** Each change in a sentence. */
  items: string[];
}

/** The notes of one release, as CHANGELOG.md has them (VEY-82). */
export interface Release {
  /** The release, such as `0.4.1`. */
  version: string;
  /** The day it was released, as `YYYY-MM-DD`, or null. */
  date: string | null;
  /** A sentence on the release as a whole, or null. */
  summary: string | null;
  /** What changed, by kind. */
  sections: ReleaseSection[];
}
