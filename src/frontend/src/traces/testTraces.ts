import type { LogEntry, Trace, TraceDurations, TraceSpan, TracesPage } from "../api/types";

/** The trace ID of the failed checkout the tests use. */
export const checkoutId = "4bf92f3577b34da6a3ce929d0e0e4736";

/** A span of the failed checkout; times in milliseconds after 12:00:00 UTC on 9 November 2026. */
export function testSpan(
  id: string,
  parent: string | null,
  service: string,
  name: string,
  start: number,
  duration: number,
  extra: Partial<TraceSpan> = {},
): TraceSpan {
  return {
    spanId: id,
    parentSpanId: parent,
    name,
    kind: parent === null ? "Server" : "Client",
    start: new Date(Date.UTC(2026, 10, 9, 12, 0, 0, start)).toISOString(),
    durationMs: duration,
    status: "Unset",
    statusMessage: null,
    service,
    environment: "production",
    host: "web-02",
    attributes: {},
    resourceAttributes: { "service.name": service },
    events: [],
    links: [],
    dropped: { attributes: 0, events: 0, links: 0 },
    depth: 0,
    children: 0,
    missingParent: false,
    ...extra,
  };
}

/**
 * A checkout that failed because psp-client timed out, with one span whose parent never arrived. The spans come in the
 * server's tree order, each with its depth (VEY-68).
 */
export const checkoutTrace: Trace = {
  traceId: checkoutId,
  spans: [
    testSpan("0000000000000001", null, "checkout-web", "POST /api/checkout", 0, 1840, {
      status: "Error",
      children: 1,
    }),
    testSpan("0000000000000002", "0000000000000001", "payments-gateway", "POST /payments", 52, 1774, {
      status: "Error",
      depth: 1,
      children: 2,
    }),
    testSpan("0000000000000004", "0000000000000002", "postgres", "SELECT payments.customer", 63, 14, { depth: 2 }),
    testSpan("0000000000000003", "0000000000000002", "psp-client", "POST /authorize", 110, 1606, {
      status: "Error",
      depth: 2,
      attributes: { "http.route": "/authorize", "http.response.status_code": "504" },
      events: [
        {
          timestamp: new Date(Date.UTC(2026, 10, 9, 12, 0, 1, 712)).toISOString(),
          name: "exception",
          attributes: {
            "exception.type": "System.TimeoutException",
            "exception.message": "The PSP did not answer within 1.5 s",
          },
        },
      ],
    }),
    testSpan("0000000000000005", "00000000000000ff", "inventory", "reserve stock", 84, 14, { missingParent: true }),
  ],
  summary: {
    start: "2026-11-09T12:00:00.000Z",
    durationMs: 1840,
    spans: 5,
    failed: 3,
    missingParents: 1,
    missingParentServices: ["inventory"],
    services: [
      { service: "checkout-web", spans: 1 },
      { service: "inventory", spans: 1 },
      { service: "payments-gateway", spans: 1 },
      { service: "postgres", spans: 1 },
      { service: "psp-client", spans: 1 },
    ],
    criticalPath: ["0000000000000001", "0000000000000002", "0000000000000003", "0000000000000004"],
    cause: "0000000000000003",
  },
};

/** The failed checkout as the server answers with `errorsOnly`: the failed spans and the spans above them. */
export const checkoutErrors: Trace = {
  ...checkoutTrace,
  spans: checkoutTrace.spans
    .filter((span) => span.status === "Error")
    .map((span) => (span.spanId === "0000000000000002" ? { ...span, children: 1 } : span)),
};

/** A page of the trace list with the failed checkout and a quick one. */
export const tracesPage: TracesPage = {
  items: [
    {
      traceId: checkoutId,
      rootSpanId: "0000000000000001",
      name: "POST /api/checkout",
      service: "checkout-web",
      environment: "production",
      start: "2026-11-09T12:00:00.000Z",
      durationMs: 1840,
      failed: true,
      spans: 27,
      failedSpans: 4,
      cause: { service: "psp-client", spanName: "POST /authorize", message: "The PSP did not answer" },
      services: [
        { service: "payments-gateway", spans: 12 },
        { service: "psp-client", spans: 8 },
      ],
    },
    {
      traceId: "00000000000000000000000000000abc",
      rootSpanId: "0000000000000009",
      name: "GET /health",
      service: "orders-api",
      environment: "production",
      start: "2026-11-09T11:59:00.000Z",
      durationMs: 3,
      failed: false,
      spans: 1,
      failedSpans: 0,
      cause: null,
      services: [{ service: "orders-api", spans: 1 }],
    },
  ],
  nextCursor: null,
};

/** The chart's points: one failed trace, one slow, one quick. */
export const durations: TraceDurations = {
  points: [
    { traceId: checkoutId, start: "2026-11-09T12:00:00.000Z", durationMs: 1840, failed: true },
    { traceId: "00000000000000000000000000000def", start: "2026-11-09T11:30:00.000Z", durationMs: 900, failed: false },
    { traceId: "00000000000000000000000000000abc", start: "2026-11-09T11:59:00.000Z", durationMs: 3, failed: false },
  ],
  read: 3,
  maxRead: 20000,
  p95Ms: 900,
  failed: 1,
  slow: 1,
};

/** A log record of the psp-client span. */
export const pspLog: LogEntry = {
  id: 7,
  timestamp: "2026-11-09T12:00:01.726Z",
  observedTimestamp: "2026-11-09T12:00:01.726Z",
  severity: "Error",
  severityText: "Error",
  body: "Authorization failed: TimeoutException after 1500 ms",
  serviceName: "psp-client",
  environment: "production",
  traceId: checkoutId,
  spanId: "0000000000000003",
  attributes: {},
  resourceAttributes: {},
};
