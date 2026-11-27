# 0024. Log queries, saved views, levels, host, and export

Date: 2026-11-26
Status: Accepted

## Context

The Console look's Log explorer (VEY-69 step 3, reference screenshot 6) has elements the API cannot serve yet:

- **A query bar.** It takes text like `service:CustomerSync AND level>=warning "timed out"`, with filter chips that
  show and change the same query.
- **Saved views.** Each person keeps their own and may share them.
- **Host.** A host filter, and a host column in the table.
- **Levels.** Log volume stacked by level, and a total for each level.
- **Export.** The whole result, not only the page the browser has loaded.

Every filter runs on the server, and every answer meets the speed budgets of ADR 0015. `storage.log_record` already
keeps `host_name`, and the minute and hour counts already keep the severity, so host and levels need no new storage.

## Decision

### 1. One query language, parsed on the server

`q` is a new parameter of `GET /api/v1/logs`, `/logs/summary`, `/logs/stream`, and `/logs/export`. The server parses
it into the filters these endpoints already take, and ANDs it with any of them given as their own parameters.

**Grammar.**

- **Terms.** A query is terms joined by `AND`. A space between terms means `AND` too. There is no `OR` or `NOT` in this
  version.
- **Field terms.** A term is `field:value`, or `level>=value` for the level.
- **Words.** A bare word, or a "quoted phrase", is matched against the message. A record matches when it has every
  word and phrase, ignoring case.
- **Quoting.** A value with spaces is quoted: `service:"Orders API"`.

**Fields.**

| Field | Meaning | Same as |
| --- | --- | --- |
| `service:` | the service; repeated, any of them | `service` |
| `env:`, `environment:` | the environment; repeated, any of them | `environment` |
| `host:` | the host; repeated, any of them | `host` (new) |
| `level:` | exactly this level; repeated, any of them | `level` (new) |
| `level>=` | this level or worse | `severity` |
| `trace:` | the trace id | `traceId` |
| `span:` | the span id | `spanId` |
| any other `key:value` | a record attribute | `attribute` |

**Errors.** A query that does not parse is answered with `400` and a problem response. The problem names the
position and what was expected, so the bar can underline it.

**Parsing for chips.** `GET /api/v1/logs/parse?q=` answers the terms, each with its field, operator, value, and
its start and end in the text. The chips show the terms, and edit the text at those positions. So the browser never
interprets the query; it only adds and removes terms the server has located.

### 2. Host and levels

- **Host.** `host` is a new filter on `storage.log_record.host_name`, repeated for any of several. Records return
  `hostName`.
- **Exact levels.** `level` is a new filter on exact severities, repeated for any of several. It sits beside the
  existing minimum, `severity`.
- **Counts by level.** `GET /api/v1/logs/summary` adds a count per level to each bucket (`levels`, keyed by severity
  name), and the window's total per level (`levels` at the top).
- **Where counts come from.** Counts are read from the minute and hour counts as today, so they stay instant. Only
  words, attributes, a trace, or a host count raw records, as words and attributes already do.

### 3. Saved views

- **What a view is.** A name, the screen it belongs to (`logs` in this version), the query text, and whether it is
  shared.
- **Where it is kept.** The Access module keeps views beside each person's look (`access.saved_view`), because a
  view belongs to a person.
- **Endpoints.** `GET`, `POST`, `PUT`, and `DELETE` under `/api/v1/views`, for any signed-in person.
- **Who sees what.**
  - A person sees their own views, and the views others shared, marked with who shared them.
  - Only the owner changes or deletes a view.
- **What a view holds.** Not the time range, which stays the shell's (ADR 0022). Opening a view keeps the range
  on screen.
- **The limit.** At most 100 views a person.

### 4. Export of the whole result

- **The endpoint.** `GET /api/v1/logs/export` takes the filters of `GET /api/v1/logs` and `format=ndjson` (default)
  or `csv`.
- **How it is written.** It streams the matching records, newest first, page by page with the existing cursor, so
  memory stays flat.
- **The limit.** It stops at `Query.MaxExportRecords` (100,000 by default, a setting Admins change on the Settings
  screen, ADR 0017). The response then ends with a last line saying the export was cut short.
- **Who may export.** Any Viewer may export, as they may read the records.

### 5. The time range

The query never holds the time; the window is the shell's range (ADR 0022), sent as `from` and `to`. A `time:` term
is an error that says so.

## Alternatives considered

- **Parse the query in the browser.** Each look would interpret it on its own, and the server would still check it.
  Two parsers drift.
- **A full Lucene or KQL grammar with OR and brackets.** It is more than the reference needs. `AND` with repeated
  fields covers the screenshot. `OR` can come later without breaking these queries.
- **Keep saved views in the browser.** They would not follow a person to another machine, and could not be shared.
- **Export only the loaded page.** That is what exists. It misleads anyone who expects the whole result.

## Consequences

- The Forecast Log Explorer keeps its filter panel and its parameters. Console sends `q`. Both reach the same
  filters on the server.
- A query in the address (`?q=…`) is a shareable link, as filters are today.
- Host filters and exact levels count raw records when words or attributes are also present, as today. A host alone
  counts raw records of the window, so a new index on `(host_name, timestamp)` of `storage.log_record` holds it to
  the speed budget.
- Export adds one setting and a long-running request, bounded by the record limit.
