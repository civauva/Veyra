import { selectableSeverities, type Severity } from "../api/types";

/** The time ranges offered as one-click choices, counted back from now. */
export const timeRanges = ["15m", "1h", "6h", "24h"] as const;

/** One of the {@link timeRanges}. */
export type TimeRange = (typeof timeRanges)[number];

const rangeMinutes: Record<TimeRange, number> = { "15m": 15, "1h": 60, "6h": 360, "24h": 1440 };

/** One `key = value` attribute filter. */
export interface AttributeFilter {
  /** The attribute name, for example `http.route`. */
  key: string;
  /** The exact value to match. */
  value: string;
}

/** Everything a person can filter the Log Explorer by. */
export interface LogFilters {
  /** How far back from now to search. */
  range: TimeRange;
  /** Only this severity or worse; null for all. */
  minimumSeverity: Severity | null;
  /** Only these services; empty for all. */
  services: string[];
  /** Words that must all appear in the message. */
  text: string;
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

/** The view a person sees before choosing anything: the last hour, everything. */
export const defaultFilters: LogFilters = {
  range: "1h",
  minimumSeverity: null,
  services: [],
  text: "",
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

  if (filters.range !== defaultFilters.range) {
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
    minimumSeverity: selectableSeverities.includes(severity as Severity) ? (severity as Severity) : null,
    services: params.getAll("service").filter((service) => service.length > 0),
    text: params.get("text") ?? "",
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
