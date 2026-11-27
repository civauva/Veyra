import { selectableSeverities, type Severity } from "../api/types";

/**
 * The time ranges offered as one-click choices, counted back from now: the shared presets of every screen (ADR 0022).
 * Logs are kept 14 days, so the last 30 days show what is kept.
 */
export const timeRanges = ["15m", "1h", "6h", "24h", "7d", "30d"] as const;

/** One of the {@link timeRanges}. */
export type TimeRange = (typeof timeRanges)[number];

const rangeMinutes: Record<TimeRange, number> = {
  "15m": 15,
  "1h": 60,
  "6h": 360,
  "24h": 1440,
  "7d": 10_080,
  "30d": 43_200,
};

/** One `key = value` attribute filter. */
export interface AttributeFilter {
  /** The attribute name, for example `http.route`. */
  key: string;
  /** The exact value to match. */
  value: string;
}

/** Everything a person can filter the Log Explorer by. */
export interface LogFilters {
  /** How far back from now to search, unless a window of one's own is set. */
  range: TimeRange;
  /** A window of one's own from the shared time range, as ISO instants; null for a range counted back from now. */
  window: { from: string; to: string } | null;
  /** Only this severity or worse; null for all. */
  minimumSeverity: Severity | null;
  /** Only these services; empty for all. */
  services: string[];
  /** Words that must all appear in the message. */
  text: string;
  /**
   * The text of the query bar, which the server parses into filters and ANDs with the others (ADR 0024); empty for
   * none. The Console look filters with it alone.
   */
  query: string;
  /** Attributes that must all match exactly. */
  attributes: AttributeFilter[];
  /** Only records of this trace. */
  traceId: string | null;
  /**
   * An ISO 8601 instant. When set, the window is one minute on either side of it instead of {@link range}, for
   * "what else happened around this record".
   */
  around: string | null;
}

/** The view a person sees before choosing anything: the last 24 hours, as every screen opens (ADR 0022), everything. */
export const defaultFilters: LogFilters = {
  range: "24h",
  window: null,
  minimumSeverity: null,
  services: [],
  text: "",
  query: "",
  attributes: [],
  traceId: null,
  around: null,
};

/** How far either side of {@link LogFilters.around} the window reaches. */
const aroundMilliseconds = 60_000;

/** How many records one request asks for. The list loads the next page as the person scrolls. */
export const pageSize = 100;

/**
 * Builds the query of `GET /api/v1/logs` (docs/api/rest.md).
 *
 * @param filters - What the person chose.
 * @param now - The end of the time window. Passed in, so a list of pages keeps one fixed window.
 * @param cursor - The `nextCursor` of the previous page, for the next one.
 * @returns The query parameters.
 */
export function filtersToApiParams(filters: LogFilters, now: Date, cursor?: string): URLSearchParams {
  const params = new URLSearchParams();
  const { from, to } = timeWindow(filters, now);

  params.set("from", from.toISOString());
  params.set("to", to.toISOString());
  params.set("limit", String(pageSize));
  addCommon(params, filters);

  if (cursor) {
    params.set("cursor", cursor);
  }

  return params;
}

/**
 * The time window the filters describe.
 *
 * @param filters - What the person chose.
 * @param now - The end of a range counted back from now.
 * @returns The start and the end of the window.
 */
export function timeWindow(filters: LogFilters, now: Date): { from: Date; to: Date } {
  if (filters.around) {
    const centre = new Date(filters.around).getTime();

    return { from: new Date(centre - aroundMilliseconds), to: new Date(centre + aroundMilliseconds) };
  }

  if (filters.window) {
    return { from: new Date(filters.window.from), to: new Date(filters.window.to) };
  }

  return { from: new Date(now.getTime() - rangeMinutes[filters.range] * 60_000), to: now };
}

/**
 * Writes the filters into the browser URL, so a link or a reload shows the same view.
 *
 * @param filters - What the person chose.
 * @returns The query string, without the leading `?`.
 */
export function filtersToUrl(filters: LogFilters): string {
  const params = new URLSearchParams();

  if (filters.window) {
    params.set("from", filters.window.from);
    params.set("to", filters.window.to);
  } else if (filters.range !== defaultFilters.range) {
    params.set("range", filters.range);
  }

  addCommon(params, filters);

  if (filters.around) {
    params.set("around", filters.around);
  }

  return params.toString();
}

/**
 * Reads filters from the browser URL. Anything unknown falls back to the default, so an old or edited link never
 * breaks the page.
 *
 * @param params - The URL's query parameters.
 * @returns The filters.
 */
export function filtersFromUrl(params: URLSearchParams): LogFilters {
  const range = params.get("range");
  const severity = params.get("severity");

  return {
    range: timeRanges.includes(range as TimeRange) ? (range as TimeRange) : defaultFilters.range,
    window: windowFrom(params),
    minimumSeverity: selectableSeverities.includes(severity as Severity) ? (severity as Severity) : null,
    services: params.getAll("service").filter((service) => service.length > 0),
    text: params.get("text") ?? "",
    query: params.get("q") ?? "",
    attributes: params
      .getAll("attribute")
      .map(parseAttribute)
      .filter((filter): filter is AttributeFilter => filter !== null),
    traceId: params.get("traceId") || null,
    around: parseInstant(params.get("around")),
  };
}

/** The filters both the API query and the browser URL carry, in the same form. */
function addCommon(params: URLSearchParams, filters: LogFilters): void {
  if (filters.minimumSeverity) {
    params.set("severity", filters.minimumSeverity);
  }

  for (const service of filters.services) {
    params.append("service", service);
  }

  if (filters.text.trim()) {
    params.set("text", filters.text.trim());
  }

  for (const attribute of filters.attributes) {
    params.append("attribute", `${attribute.key}:${attribute.value}`);
  }

  if (filters.traceId) {
    params.set("traceId", filters.traceId);
  }

  if (filters.query.trim()) {
    params.set("q", filters.query.trim());
  }
}

/** Splits `key:value` at the first colon, as the API does, so values may contain colons. */
function parseAttribute(text: string): AttributeFilter | null {
  const separator = text.indexOf(":");

  return separator < 1 ? null : { key: text.slice(0, separator), value: text.slice(separator + 1) };
}

/** Accepts only a valid instant and normalises it, so an edited link cannot produce an invalid window. */
function parseInstant(text: string | null): string | null {
  const time = text ? Date.parse(text) : Number.NaN;

  return Number.isNaN(time) ? null : new Date(time).toISOString();
}

/**
 * A window of one's own from the address, when it names a valid `from` and `to` and no preset.
 *
 * @param params - The address's query parameters.
 * @returns The window, or null.
 */
function windowFrom(params: URLSearchParams): { from: string; to: string } | null {
  const from = params.get("from");
  const to = params.get("to");

  if (params.has("range") || from === null || to === null) {
    return null;
  }

  const start = Date.parse(from);
  const end = Date.parse(to);

  return Number.isNaN(start) || Number.isNaN(end) || start >= end ? null : { from, to };
}
