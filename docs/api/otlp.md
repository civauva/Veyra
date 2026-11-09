# OTLP endpoints

Veyra receives telemetry over OTLP only. Today it accepts logs; metrics and traces will follow on the same
endpoints. The behaviour follows the [OTLP specification](https://opentelemetry.io/docs/specs/otlp/).

## Where to send

| Transport | Address on the server | Through Caddy (TLS) |
| --- | --- | --- |
| OTLP/gRPC | `http://127.0.0.1:4317` | `https://<host>` |
| OTLP/HTTP | `http://127.0.0.1:4318/v1/logs` | `https://<host>/v1/logs` |

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

## OTLP/gRPC: `opentelemetry.proto.collector.logs.v1.LogsService/Export`

| Status | Meaning |
| --- | --- |
| `OK` | Records queued; `partial_success` lists refused records. |
| `UNAUTHENTICATED` | The key is missing, unknown, revoked, or expired. |
| `UNAVAILABLE` | Veyra is busy. The SDKs retry this automatically. |
| `INVALID_ARGUMENT` | The message could not be read. |
| `RESOURCE_EXHAUSTED` | Too many wrong keys from this address this minute. |

## What happens next

Accepted records are queued in memory and written shortly after. The details, including when records can be lost,
are in [`docs/architecture/ingestion.md`](../architecture/ingestion.md).
