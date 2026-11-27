import type { AttributeFilter } from "../logs/logFilters";

/** The time ranges offered as one-click choices, counted back from now. Traces keep 7 days (ADR 0014). */
export const traceRanges = ["15m", "1h", "6h", "24h", "7d", "30d"] as const;

/** One of the {@link traceRanges}. */
export type TraceRange = (typeof traceRanges)[number];

/**
 * Which traces to show: all, those whose request failed, those as slow as the slowest 5%, or those that are both
 * (ADR 0029).
 */
export type TraceShow = "all" | "failed" | "slow" | "failed-slow";

/** The order of the list (ADR 0029): newest first, or worst first, failed and then the slowest. */
export type TraceOrder = "newest" | "worst";

/** A trace ID as people paste it: 32 hexadecimal characters. */
const traceIdPattern = /^[0-9a-f]{32}$/i;

/**
 * Whether text typed into a trace search is a trace ID to open rather than a name to filter by.
 *
 * @param text - What the person typed.
 * @returns True for 32 hexadecimal characters, spaces around them aside.
 */
export function isTraceId(text: string): boolean {
  return traceIdPattern.test(text.trim());
}

// Traces are kept 7 days, so the last 30 days show what is kept (ADR 0022).
const rangeMinutes: Record<TraceRange, number> = {
  "15m": 15,
  "1h": 60,
  "6h": 360,
  "24h": 1440,
  "7d": 10_080,
  "30d": 43_200,
};

/** Everything a person can filter the trace list by. */
export interface TraceFilters {
  /** How far back from now to look, unless a window was picked on the chart. */
  range: TraceRange;
  /** All traces, failed ones, or slow ones. */
  show: TraceShow;
  /** Only traces whose request came to one of these services; empty for all. */
  services: string[];
  /** Only traces that took at least this many milliseconds, or null. */
  minMs: number | null;
  /** Only traces that took at most this many milliseconds, or null. */
  maxMs: number | null;
  /** Only traces whose name contains this. */
  name: string;
  /** Attributes the root span must have. */
  attributes: AttributeFilter[];
  /** The start of a window picked on the chart, in ISO 8601; null to count back from now. */
  from: string | null;
  /** The end of that window. */
  to: string | null;
  /** The order the server lists the traces in. */
  order: TraceOrder;
}

/** The view a person sees first: the last 24 hours, as every screen opens (ADR 0022), every trace. */
export const defaultTraceFilters: TraceFilters = {
  range: "24h",
  show: "all",
  services: [],
  minMs: null,
  maxMs: null,
  name: "",
  attributes: [],
  from: null,
  to: null,
  order: "newest",
};

/**
 * The window the list shows: the window picked on the chart, or the range counted back from now.
 *
 * @param filters - What the person chose.
 * @param now - The end of a range window.
 * @returns The start and the end.
 */
export function traceWindow(filters: TraceFilters, now: Date): { from: Date; to: Date } {
  if (filters.from && filters.to) {
    return { from: new Date(filters.from), to: new Date(filters.to) };
  }

  return { from: new Date(now.getTime() - rangeMinutes[filters.range] * 60_000), to: now };
}

/**
 * Reads the filters from the address, so a view can be linked and survives a reload. Anything it does not
 * understand falls back to the default.
 *
 * @param params - The query string of the address.
 * @param order - The order when the address names none: newest first, or worst first on a screen that opens on it.
 * @returns The filters.
 */
export function filtersFromSearch(params: URLSearchParams, order: TraceOrder = "newest"): TraceFilters {
  const named = params.get("order");
  const range = params.get("range");
  const show = params.get("show");
  const number = (name: string) => {
    const value = Number(params.get(name));

    return params.has(name) && Number.isFinite(value) && value >= 0 ? value : null;
  };
  const from = params.get("from");
  const to = params.get("to");
  const picked = from !== null && to !== null && !Number.isNaN(Date.parse(from)) && !Number.isNaN(Date.parse(to));

  return {
    range: traceRanges.includes(range as TraceRange) ? (range as TraceRange) : defaultTraceFilters.range,
    show: show === "failed" || show === "slow" || show === "failed-slow" ? show : "all",
    services: params.getAll("service"),
    minMs: number("min"),
    maxMs: number("max"),
    name: params.get("name") ?? "",
    attributes: params
      .getAll("attribute")
      .map((pair) => ({ key: pair.slice(0, pair.indexOf(":")), value: pair.slice(pair.indexOf(":") + 1) }))
      .filter((attribute) => attribute.key.length > 0),
    from: picked ? from : null,
    to: picked ? to : null,
    order: named === "worst" || named === "newest" ? named : order,
  };
}

/**
 * Writes the filters into the address, leaving defaults out so a plain view keeps a plain address.
 *
 * @param filters - What the person chose.
 * @param order - The screen's own default order, which the address leaves out.
 * @returns The query string.
 */
export function filtersToSearch(filters: TraceFilters, order: TraceOrder = "newest"): URLSearchParams {
  const params = new URLSearchParams();

  if (filters.range !== defaultTraceFilters.range) {
    params.set("range", filters.range);
  }

  if (filters.show !== "all") {
    params.set("show", filters.show);
  }

  filters.services.forEach((service) => params.append("service", service));

  if (filters.minMs !== null) {
    params.set("min", String(filters.minMs));
  }

  if (filters.maxMs !== null) {
    params.set("max", String(filters.maxMs));
  }

  if (filters.name) {
    params.set("name", filters.name);
  }

  filters.attributes.forEach((attribute) => params.append("attribute", `${attribute.key}:${attribute.value}`));

  if (filters.from && filters.to) {
    params.set("from", filters.from);
    params.set("to", filters.to);
  }

  if (filters.order !== order) {
    params.set("order", filters.order);
  }

  return params;
}

/**
 * Builds the query of the trace endpoints (docs/api/rest.md).
 *
 * @param filters - What the person chose.
 * @param now - The end of a range window; passed in, so the list, chart, and services share one window.
 * @param environment - The environment chosen in the top bar, or null for all.
 * @param cursor - The `nextCursor` of the previous page, for the next one.
 * @returns The query parameters.
 */
export function filtersToApiParams(
  filters: TraceFilters,
  now: Date,
  environment: string | null,
  cursor?: string,
): URLSearchParams {
  const params = new URLSearchParams();
  const { from, to } = traceWindow(filters, now);

  params.set("from", from.toISOString());
  params.set("to", to.toISOString());
  filters.services.forEach((service) => params.append("service", service));

  if (environment) {
    params.set("environment", environment);
  }

  if (filters.show === "failed" || filters.show === "failed-slow") {
    params.set("failed", "true");
  }

  if (filters.show === "slow" || filters.show === "failed-slow") {
    params.set("slow", "true");
  }

  // Newest first is the server's own order, so only worst first is asked for (ADR 0029).
  if (filters.order === "worst") {
    params.set("order", "worst");
  }

  if (filters.minMs !== null) {
    params.set("minDurationMs", String(filters.minMs));
  }

  if (filters.maxMs !== null) {
    params.set("maxDurationMs", String(filters.maxMs));
  }

  if (filters.name) {
    params.set("name", filters.name);
  }

  filters.attributes.forEach((attribute) => params.append("attribute", `${attribute.key}:${attribute.value}`));

  if (cursor) {
    params.set("cursor", cursor);
  }

  return params;
}
