# REST API

The API the Veyra UI uses, and that scripts can use too. All paths start with `/api/v1`. JSON uses camelCase,
times are ISO 8601 in UTC, enums are strings, and errors are problem details (RFC 9457).

## Signing in

Everything except the endpoints marked **open** requires a session (ADR 0008):

1. `POST /api/v1/session` with `{"email", "password"}` sets the session cookie.
2. `GET /api/v1/session/antiforgery` returns `{"token"}`.
3. Every request that changes something sends that token in the `X-Veyra-Antiforgery` header.

Sign-in needs HTTPS outside development, because the cookie is `Secure`.

## Endpoints

| Method and path | Who | What |
| --- | --- | --- |
| `GET /api/v1/setup` | open | Whether first-run setup is still needed |
| `POST /api/v1/setup` | open | Creates the first administrator with the token from the log |
| `POST /api/v1/session` | open | Signs in |
| `GET /api/v1/session` | signed in | The current user |
| `DELETE /api/v1/session` | signed in | Signs out |
| `GET /api/v1/session/antiforgery` | signed in | The antiforgery token |
| `PUT /api/v1/session/password` | signed in | Changes your own password |
| `GET /api/v1/logs` | Viewer | Searches log records |
| `GET /api/v1/logs/summary` | Viewer | Counts records over time and per service |
| `GET /api/v1/users` | Admin | Lists users |
| `POST /api/v1/users` | Admin | Creates a local user |
| `PATCH /api/v1/users/{id}` | Admin | Changes a user's role or disabled flag |
| `GET /api/v1/ingestion-keys` | Admin | Lists ingestion keys, without secrets |
| `POST /api/v1/ingestion-keys` | Admin | Creates a key; the response holds the secret once |
| `DELETE /api/v1/ingestion-keys/{id}` | Admin | Revokes a key |

## Searching logs: `GET /api/v1/logs`

| Parameter | Default | Meaning |
| --- | --- | --- |
| `from`, `to` | the last hour | The time window, at most 31 days |
| `severity` | all | The lowest severity, for example `Warning` |
| `service` | all | Only these services; repeat for several |
| `text` | none | Words that must all appear in the message, ignoring case |
| `traceId` | none | Only records of this trace |
| `environment` | all | Only these environments; repeat for several |
| `attribute` | none | `key:value`, for example `http.route:/pay`; repeat to require several |
| `limit` | 100 | Page size, 1 to `Veyra:Query:MaxPageSize` (1,000) |
| `cursor` | none | `nextCursor` from the previous page |

The answer is `{"items": [...], "nextCursor": "..."}`, newest first. `nextCursor` is `null` on the last page.

## Counting logs: `GET /api/v1/logs/summary`

Takes the same filters as `GET /api/v1/logs`, without `limit` and `cursor`, and answers with the counts behind the
Log Explorer's histogram and service filter:

```json
{
  "bucketSeconds": 120,
  "buckets": [{ "start": "2026-11-09T06:34:00Z", "total": 118, "errors": 2 }],
  "services": [{ "name": "payments-gateway", "count": 4812 }]
}
```

- `buckets` covers the whole window, oldest first, with empty buckets included. The width is the first of 30 s,
  1, 2, 5, 10, 15, and 30 min, 1, 2, 6, and 12 h, or 1 day that gives at most 40 buckets.
- `errors` counts records at `Error` or worse.
- `services` lists the 50 busiest services, most first. It ignores the `service` filter, so the UI can show what
  else there is to pick.
