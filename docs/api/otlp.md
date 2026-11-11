# OTLP endpoints

Veyra receives telemetry over OTLP only. Today it accepts logs and metrics; traces will follow on the same
endpoints. The behaviour follows the [OTLP specification](https://opentelemetry.io/docs/specs/otlp/).

## Where to send

| Transport | Address on the server | Through Caddy (TLS) |
| --- | --- | --- |
| OTLP/gRPC | `http://127.0.0.1:4317` | `https://<host>` |
| OTLP/HTTP | `http://127.0.0.1:4318/v1/logs`, `/v1/metrics` | `https://<host>/v1/logs`, `/v1/metrics` |

Senders on the Veyra server itself, such as a Collector watching the host, can use the local ports. Everything else
goes through Caddy, so the key never travels unencrypted.

## Authentication

Every request carries an ingestion key (ADR 0008), created by an administrator under `/api/v1/ingestion-keys`:

- OTLP/HTTP: the header `Authorization: Bearer vyk_...`.
- OTLP/gRPC: the metadata `authorization: Bearer vyk_...`.

In the SDKs' environment configuration that is `OTEL_EXPORTER_OTLP_HEADERS=Authorization=Bearer%20vyk_...`.

## OTLP/HTTP: `POST /v1/logs`

- **Body.** `application/x-protobuf` or `application/json` (OTLP/JSON, with hex trace and span IDs). Add
  `Content-Encoding: gzip` for compressed bodies. At most 16 MB, also after decompression.
- **Response.** The same format as the request: an `ExportLogsServiceResponse`, with `partialSuccess` when records
  were refused.

| Status | Meaning | Sender should |
| --- | --- | --- |
| `200` | Records queued. Check `partialSuccess` for refused records. | Continue |
| `400` | The body is not a valid OTLP logs request. | Fix the sender; do not retry |
| `401` | The key is missing, unknown, revoked, or expired. | Fix the key; do not retry |
| `413` | The body is larger than 16 MB. | Send smaller batches |
| `415` | The content type is neither protobuf nor JSON. | Fix the sender |
| `429` | Too many wrong keys from this address this minute. | Fix the key; wait a minute |
| `503` | Veyra is busy; see `Retry-After`. | Retry with back-off |

## OTLP/HTTP: `POST /v1/metrics`

The same bodies, statuses, and limits as `/v1/logs`, with an `ExportMetricsServiceRequest` and an
`ExportMetricsServiceResponse`.

- **Stored.** Gauges, sums (cumulative or delta), and cumulative explicit-bucket histograms. See ADR 0010.
- **Refused.** Exponential histograms, summaries, and delta histograms. They are counted in
  `partialSuccess.rejectedDataPoints`, and `errorMessage` says why. The standard SDKs and the Collector send none
  of them by default.

## OTLP/gRPC: `opentelemetry.proto.collector.logs.v1.LogsService/Export`

| Status | Meaning |
| --- | --- |
| `OK` | Records queued; `partial_success` lists refused records. |
| `UNAUTHENTICATED` | The key is missing, unknown, revoked, or expired. |
| `UNAVAILABLE` | Veyra is busy. The SDKs retry this automatically. |
| `INVALID_ARGUMENT` | The message could not be read. |
| `RESOURCE_EXHAUSTED` | Too many wrong keys from this address this minute. |

## OTLP/gRPC: `opentelemetry.proto.collector.metrics.v1.MetricsService/Export`

The same statuses as for logs; `partial_success` counts the refused data points.

## What happens next

Accepted records and points are queued in memory and written shortly after. The details, including when records can be lost,
are in [`docs/architecture/ingestion.md`](../architecture/ingestion.md).
