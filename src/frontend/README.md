# Frontend

React + TypeScript single-page application built with Vite (ADR 0004). The production build is a set of static files
that `Veyra.Api` serves from the same container; the app talks to the backend only through the REST API
([`docs/api/rest.md`](../../docs/api/rest.md)).

## Screens

| Path                                 | Who                                          | What                                                                                                             |
| ------------------------------------ | -------------------------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| `/setup?token=…`                     | anyone, until the first administrator exists | First-run setup with the link from Veyra's log                                                                   |
| sign-in                              | anyone                                       | Local sign-in; shown whenever there is no session                                                                |
| `/`                                  | Viewer                                       | Forecast home: weather, Needs you, key metrics, hourly strip, estate, outlook                                    |
| `/logs`                              | Viewer                                       | Log Explorer: filters, message search, histogram, result list, the selected record                               |
| `/traces`                            | Viewer                                       | Traces: filters, a duration chart to pick from, one row per trace                                                |
| `/traces/:traceId?at=…`              | Viewer                                       | One trace: waterfall with the critical path, the selected span, the trace's logs                                 |
| `/alerts`                            | Viewer                                       | Alerts: 30 or 90 days of history, a weather log, filters, and the built-in rules                                 |
| `/alerts/:id`                        | Viewer                                       | One alert: its rule's chart, timeline with notifications, errors, disks, or routes                               |
| `/metrics`                           | Viewer                                       | Metrics overview: a card per host and service, and the metric browser                                            |
| `/metrics/hosts/:host`               | Viewer                                       | One host: CPU, memory, disks with when they fill, network, disk I/O, load                                        |
| `/metrics/databases/:engine/:server` | Viewer                                       | One database server: blocking, deadlocks, waits, databases, jobs, queries now                                    |
| `/metrics/services/:service`         | Viewer                                       | One service: latency, requests, routes, CPU and memory, runtime                                                  |
| `/jobs`                              | Viewer                                       | Jobs and pipelines: the figures, each job's last run, success share, and latest runs                             |
| `/jobs/:id`                          | Viewer                                       | One job: its figures, run durations, and runs with their steps, trace, and logs                                  |
| `/keys`                              | Admin                                        | Ingestion keys: create (the secret is shown once), list, revoke                                                  |
| `/users`                             | Admin                                        | Users: security rings, second-factor rule, person badges, and a panel per person                                 |
| `/settings?section=…`                | Admin                                        | Settings: every module's settings, review, history, export and import; Database monitoring and Agents (ADR 0019) |
| `/account`                           | Viewer                                       | Your account: password, second factor, recovery codes, sessions, sign-ins                                        |
| `/notifications`                     | Admin                                        | Webhook and email channels: add, change, switch off, test, delete                                                |
| `/integrations`                      | Admin                                        | Integrations: each SQL Server's state, template version, and modules; the wizard and each package page           |

The trace list asks for the page, the duration chart, and the services together, for one window
(`traces/useTraceList.ts`), and looks again every 30 seconds while the window ends now. The trace view reads the trace
every 30 seconds too, because spans of other services may still be arriving, and says when part of it is missing
(ADR 0014).

The Log Explorer asks for the records and the histogram's counts together and shows each as it arrives
(`logs/useLogSearch.ts`): the list never waits for the counts, and the histogram says it is counting until they come.

The shell asks for the home snapshot every 15 seconds on every screen (`app/useShellStatus.ts`) and keeps the last
picture when a refresh fails. The Forecast look draws the sidebar of every screen (`looks/forecast/Sidebar.tsx`,
ADR 0013) with the environment switcher and the open alerts, and the weather behind it
(`looks/forecast/Weather.tsx`): full on the home and alert screens, a calm band on work screens. The sidebar is
labelled from 1600 px, icons from 1024 px, and a drawer below that (`looks/forecast/useSidebar.ts`); collapsing or
expanding it is kept in this browser. Until a person picks an environment, production is shown when there is one,
otherwise all environments; the choice is kept in this browser, and the home, Alerts, Logs, and Metrics follow it.
The shell's navigation links move between screens through a view transition
(`looks/forecast/useBlendedNavigation.ts`). "Open logs" and "± 1 minute" on an alert lead into the Log Explorer,
"Details" to the alert's own screen.

Console is the default look (ADR 0029). The Console look (VEY-71, VEY-72) draws a sidebar grouped into Observe, Resources, Respond, and Manage, and a top bar
with the environment, a search across every signal, the time range, the refresh, Live, the open alerts, help, and the
person's menu (`looks/console/Shell.tsx`). Its sidebar is labelled
from 1180 px, icons from 720 px, and a drawer below that (`looks/console/useConsoleForm.ts`). While it is drawn it sets
`data-look="console"` on the page, which switches the colour tokens to Console's (`looks/console/console.css`). Which
look a person sees comes from the session: their own choice on the Account page, or the default an Admin sets in
Settings → Look and feel, where Admins also decide whether people may choose (`api/looks.ts`,
`account/LookPanel.tsx`, `account/useLookChoice.ts`). A session that names no look this app knows falls back to Console.

Every screen shows one time range (ADR 0022, `app/time.ts`): it lives in the address as `range=24h` or as `from`
and `to`, every menu link carries it, and the shell gives screens its window and the refresh interval through the
shell contract (`app/useShellTime.ts`). The data hooks ask again on that interval instead of timers of their own;
Live asks every 5 seconds and shows the rate the server streams (`app/useLiveRate.ts`). The search asks the server
once typing pauses and shows what it found in groups (`app/useSearch.ts`). Console answers to Ctrl K (search), T (time
range), L (Live), and ? (the shortcuts).

Where the range is chosen depends on the look. Console's top bar is the only place: its shell sets `timeInBar` in the
shell contract, and the screens' own range buttons (`metrics/RangePicker.tsx`, the Time sections of the Logs and
Traces filters) are left out, the filters keeping only a line that says which window they search. Console's Alerts
read the top bar's range in whole days, at least one and at most the 90 days kept (`alerts/alertQuery.ts`). Forecast
has no range in its top bar, so its screens keep their buttons, which set the same shared range. On phones
Console's time button keeps the range in short, as `24h`, so it stays in sight there too.

The Console look's home is the Overview (`overview/`, ADR 0023): the figures with their change against the period
before, needs attention, request activity, telemetry ingestion, the applications, infrastructure, and databases worst
first, the job runs, the hosts' resources, and the collectors, all from one read of `GET /api/v1/overview`.

Every screen's stylesheet reads shape tokens from `styles/tokens.css` (corner radii, control heights, panel padding
and gaps, heading, body, and figure sizes). Forecast keeps its roomy values; Console sets the dense values of its
mockups, so screens not yet rebuilt for Console still sit well in it.

Resources → Services, Hosts & containers, and Databases (`resources/`) list everything Veyra knows, worst first, with
the header figures and their changes against the period before. The server filters, counts, sorts, and pages them;
the filters stay in the address, and silent hosts and services stay listed.

The alert screen paints the alert's weather, and clear skies once it resolved. It asks again every 15 seconds while
the alert is open. Its chart follows the width of its panel, so labels stay readable on a phone: the error rate with
the most frequent errors, request durations with the slowest routes, or a disk's week with its forecast to full and
the host's other disks. The Alerts screen asks again every 30 seconds while it shows its first page, keeps its
filters in the URL, and words every rule the same way as the home (`alerts/ruleWords.ts`). The Notifications
screen never shows a stored webhook URL; changing a webhook without typing a new URL keeps the stored one.

The Metrics screens ask again every 30 seconds and keep their range (1h, 6h, 24h, 7d), tab, search, and the
browser's metric and split in the URL. Their charts are drawn as SVG by `metrics/LineChart.tsx` and follow the width
of their panel; each chart's accessible name sums up its lines. A host card glows when one of its disks fills up
within 30 days.

Signing in asks for a code from the authenticator app after the password when the account has a second factor, or
sets one up right there when an Admin requires it. The Users screen keeps the selected person and tab in the URL; on
tablets and phones the person opens as a sheet. The person at the foot of the sidebar opens a menu with the Account
page, the theme, and signing out. QR codes are drawn as dark on white in both themes, because phone cameras read
that best.

The Log Explorer keeps its filters in the URL, so a link or a reload shows the same view. "Follow new" asks for new
records every 5 seconds. "± 1 minute, this service" switches to a fixed window around the selected record.

Console has a Log explorer of its own (`looks/console/ConsoleLogs.tsx`, VEY-74, ADR 0024). Every filter is one query in
the query bar, `q` in the address, which the server parses. The chips show the terms the server found
(`GET /api/v1/logs/parse`, `logs/useLogQueryTerms.ts`), and add, replace, or remove a term at the place the server
named (`logs/queryText.ts`), so the browser never interprets the query. A query the server refuses is marked, with
the position and what it expected. The volume chart is stacked by level, and By level filters by the level chosen.
Saved views (`logs/useSavedViews.ts`) keep the query, not the time range, and may be shared with everyone. Export
asks the server for every matching record as NDJSON or CSV (`logs/exportLink.ts`). The inspector has Details
(exception, stack trace, attributes, correlation), Related (the trace's records), Trace, and JSON tabs, with Previous
and Next.

Console's SQL Server screen (`looks/console/database/`, VEY-75, ADR 0025) has Overview, Blocking, Backups, and
Storage of its own, and the shared Live, Queries, Jobs, Events, and Server tabs. Overview, Blocking, and Storage
draw one read of the server (`GET …/overview`, `metrics/database/useServerOverview.ts`): the machine with its OS and
memory, figures with their change against the period before, performance, core counters, I/O, waits, storage,
the inventory, the blocking chains, and which monitoring areas deliver data. Backups draws the backup history
(`GET …/backups`): freshness per database against the backup rules, time per day, the last 7 days, and each
backup. A PostgreSQL server keeps the shared screen. Console files a server's and a host's screens under Resources,
so the menu marks Databases or Hosts there (`looks/console/nav.ts`).

Incidents (`incidents/`, VEY-76, ADR 0026) group related alerts into one thing to work on; the server groups them.
Every look lists them and opens one (`incidents/IncidentsScreen.tsx`, `IncidentScreen.tsx`), so a notification's
link works anywhere; Editors and Admins acknowledge them. Console draws its own (`looks/console/incidents/`), after
reference screenshot 7: the figures, the execution path that failed with its exception, one timeline of the
alerts, their notifications, and other signals, the charts and logs of the affected blocks
(`GET /api/v1/incidents/{id}/context`), and what to check, from each rule's own guidance. View runbook opens the
rule's runbook link when an Admin set one, and the guidance otherwise.

Console draws Traces, a trace, Metrics, an alert, and Settings of its own (`looks/console/traces/`, `metrics/`,
`alerts/`, `settings/`, VEY-79, ADR 0029); Forecast keeps the shared screens.

- **Traces** open worst first: failed, then the slowest. Show (all, failed, slow, failed and slow), Service, Duration,
  Order, the attributes, and the operation search are address parameters (`traces/traceFilters.ts`; `order=newest` when
  it is not the screen's default). Five figures come from `GET /api/v1/traces/summary`, the slowest services from
  `GET /api/v1/traces/services?order=slowest` (a click filters by one), and the list pages with Load more
  (`traces/useTraceList.ts` with `summary` and `slowest`). The duration chart is the shared one, with drag to narrow and
  click to open.
- **A trace** reads through `traces/useTrace.ts`: its head, the figures of the trace itself (duration, spans and levels,
  errors with the cause's exception, the critical path's share, `traces/traceFigures.ts`), the waterfall with the
  critical path marked and Errors only (`errorsOnly`), the chosen span with its attributes, exception, and records, the
  services, the trace's records, and in Advanced its resource.
- **Metrics** has Overview, a table per kind of block from the overview read (`metrics/useMetricsOverview.ts`), and
  Explorer (`metrics/useMetricExplorer.ts`): the names the server ranks, what to show, the split, and the service and
  host, all in the address and sent to `GET /api/v1/metrics/chart`, with each line's latest, average, and highest value
  as the server reads them.
- **An alert** shows figures chosen by its kind (`alerts/alertFigures.ts`), the rule's chart, what it means and how to
  fix it, the logs around it and the blocks it touches from `GET /api/v1/alerts/{id}/context`
  (`alerts/useAlertContext.ts`), its rule, its incident when it has one, and in Advanced its timeline and breakdowns.
- **Settings** draws the shared state and actions (`settings/useSettingsEditor.ts`, the controls and notes of
  `settings/SettingField.tsx`) as section navigation, panels of rows, and a save bar that stays in view; Look and feel
  offers every look that exists as a card.

Jobs and pipelines (`jobs/`, ADR 0027) are the runs the server recognises: root spans that carry a job name, and
SQL Server Agent jobs. `/jobs` lists them for the shell's range and environment, failing ones first, and `/jobs/:id`
shows one job with its runs (`GET /api/v1/jobs`, `GET /api/v1/jobs/{id}`, `jobs/useJobs.ts`). The search, status, and
source are request parameters kept in the address as `q`, `status`, and `source`; the search goes to the server once
typing pauses (`jobs/useJobChoices.ts`). Every look gets the plain screens (`jobs/JobsScreen.tsx`, `JobScreen.tsx`);
Console draws its own under Resources (`looks/console/jobs/`): the figures, a table with each job's last run, usual
duration, success share, and a strip of its latest 20 runs, and for one job a chart of every run's duration by status
and the runs newest first. Picking a run (`run=` in the address) shows its steps as an execution path, its records,
retries, and message, and leads to its trace, or says the trace has expired after the spans' 7 days, and to the logs
of its service around it. The Overview's pipeline executions open a job's screen when the server names the job.

Integrations (`integrations/`, ADR 0028) are the SQL Servers Veyra's agents read, each from a versioned template
with modules that have their own switch and interval. `/integrations` lists them with their state, template version
against the current one, modules, and agent (`GET /api/v1/integrations`); `/integrations/sqlserver/:id` is a server's
package page, and `/integrations/sqlserver/new` adds one. They are for Admins. Console draws them after reference
screenshots 2 and 4 (`looks/console/integrations/`): a five-step wizard (Connection with a default or named instance
and SQL, NTLM, or, on a Windows agent, integrated sign-in; Permissions with the login script; Modules; Validation;
Installation, which saves and follows the server until it is Receiving), and the package page with the template's
version, the Collector, configuration validation, the metrics each module collected, the time SQL Server spends on each
check, the server's key health from its overview read, Update, Roll back, Remove (after asking), the next steps, and
Configuration, Permissions, and History tabs. Test connection and Validate now start a probe through the agent
(`POST /api/v1/integrations/probes`) and ask how it stands every 3 seconds, for up to 2 minutes, showing the time it has
taken (`integrations/useIntegrations.ts`); the screen says first that the agent's Collector restarts twice for a few
seconds. The wizard's rules for each step live in `integrations/wizard.ts`. Other looks get plain screens and keep
adding servers on Settings → Database monitoring, whose SQL Server cards link to the same package page.

Times and days are shown in each person's zone (VEY-22, `time/zone.ts`): the one kept on the account from the
Account page's Times section, or the browser's until they pick one. The account menu switches every screen to UTC in
this browser. Column heads and captions name the zone, hovering a log record's time shows it in UTC, and copied
records and exports keep UTC instants. The alert summary's days start at the person's midnight.

Every screen works on desktop, tablet, and phone, in light and dark themes, and with the keyboard. In the Log
Explorer the arrow keys move the selection and Escape closes the record or the filter sheet.

A chart fills its tile: `metrics/LineChart.tsx` takes the tile's width and height, with its `height` as the least, so a
tile made taller by its row has no empty band below the chart, and its labels stay inside the plot. A range without
data says so instead of drawing a made-up scale. A tile's own class gives the chart room from the tile's edges, and a
table too wide for its tile scrolls inside it. `e2e/chart-tiles.spec.ts` checks, on every screen with charts, small
lines, or tables, at each size and in both looks, that labels and legends keep their distance from the tile's edges,
that no text runs out of its tile or under a small line, and that a chart ending its tile fills it (VEY-80).

The Overview fills a large screen (VEY-81): its rows of panels share the height below the figures, each at least as
tall as its content. Its tables and Needs attention measure the room below their last row (`overview/useFitRows.ts`)
and ask the server for as many rows as fit, never fewer than what fits a laptop: the services and hosts lists by
their `limit`, the databases and pipelines by the Overview read's `databases` and `pipelines`. They do not ask for
fewer when the window shrinks; the screen then scrolls.

Tiles drill in (VEY-82): every figure, chart, and row that has more behind it opens it, keeping the range
(`time/carryTime.ts`). A tile's name is its link or button, stretched over the whole tile by a `::after` above the
tile's drawings, with its question mark above that; a screen reader hears the name, and a click anywhere on the tile
opens it. The Overview's figures, panel titles, chart panels, and rows open their screens; the figures on Alerts and
Jobs set the list's filter; a SQL Server's overview figures open their tab.

The version sits at the foot of the sidebar in every look and opens the release notes (`about/`, ADR 0030). The shell
reads `GET /api/v1/about` when the page loads, every five minutes, and when the page is shown again; when the build
differs from the one it loaded, a notice in the corner offers What's new, Later, and Reload, without taking the focus.

Every tile and panel that shows a figure explains itself with a question mark beside its name (`ui/HelpTip.tsx`,
VEY-60): it opens on hover, a click or tap, or keyboard focus, and closes with Escape or a click elsewhere. Cards
repeated in a grid have one question mark on their section's heading. Each explanation says what the figure is, where
it comes from, and what to look for. An alert's details also say what it means and how to fix it, with scripts to
copy. `e2e/help.spec.ts` opens every question mark of every screen at each size and in both themes.

### Simple and Advanced

Every screen but the home has a bar across its top (`ui/ScreenBar.tsx`, VEY-62): where the screen is, as a trail
with links back up, on the left, and its controls, such as the time range, on the right. Settings and the monitoring
screens end the bar with the **Simple / Advanced** toggle (`ui/ViewToggle.tsx`), always in the same place, whatever
the screen, tab, or view. On a phone the trail keeps its last two steps on a row of its own, the controls under it.
Console shows the choice as one **Advanced** switch (`ViewSwitch`, `viewSwitch` in the shell contract) and gives it a
place in its top bar, beside the time range and Live (`viewSlot`); ScreenBar puts it there. On phones, where the top
bar has no room, it stays at the end of the screen's bar.

A database server opens on its **Overview** (VEY-62): one weather card per area, worst first, from the server's open
alerts and its figures, each opening its tab. The server's name in the trail switches to another server on the same
tab. Its alert banner closes with ×, and stays closed until another alert opens or one changes severity. Tiles that
lead somewhere, such as Blocked, say where and open it.

From 1800 px wide, a host, service, or database server screen lists its open alerts in a rail on the right instead of
the banner (`metrics/BlockPage.tsx`, VEY-63): the worst and then the oldest first, six at most, each opening its
details, and the rest on the Alerts screen. The rail holds nothing else, so narrower windows lose nothing: the banner
says the same. The rail and the banner hide together, and the content keeps up to its 1360 px beside the rail.

A database server's bar has a **System** switch before Simple and Advanced (`metrics/database/SystemFilter.tsx`,
VEY-64), off until someone turns it on, for every server and tab in that browser. Every choice a screen offers goes to
the API, and the server filters and ranks (VEY-66); the browser never hides or re-sorts the rows it receives. The
System choice is sent as `system=true`; off, the server leaves out the rows it keeps for itself (its own databases,
catalog statements, its jobs, idle waits) and counts them, and a tab says under its list how many with `SystemHidden`,
naming any that has an open alert. On, they show with a `system` tag. The Queries tab's Rank by and the Live tab's
filters are request parameters too. `useMetricData` takes them as `choices`: a change asks again at once and keeps the
last answer on screen until the new one arrives.

The same holds on every screen (VEY-67): the server does the data work and the browser lays out what it receives. The
Metrics overview sends its search and the chosen environment and draws the `summary` the server counts; Users sends
its search and chip and draws the server's chip counts, rings, and role bar; Settings asks
`/api/v1/settings/listing` which settings and sections to show, keeping the page of every setting as the model it
edits; a trace sends `errorsOnly` and asks for the chosen span's records by `spanId`, and takes its figures, critical
path, and cause from the server; the environment switcher sends its find box. On a database server's screen the
Simple view is a request parameter too, `simple=true`: the server keeps Queries to the statements with a finding,
Jobs to those that need a look, Events to those that need something, and the index advice to its five worst.

The server also sorts, ranks, counts, and works out the figures a screen tells (VEY-68), so nothing is lost past a
limit and no rule lives in the browser. A block's screen asks the alerts for exactly its `block` (and a host's
containers for their `part`) with `order=worst`, and draws them in that order; the database Overview's areas come
ranked from `/api/v1/alerts/areas`, and each alert says its `area`. The waterfall gets its spans in tree order with
their depth and only hides the spans under a collapsed one; a trace's records come `order=oldest`. The trace list
draws the server's `failed` and `slow` counts, the person panel the server's `signInDays`, the home its `estate`
(services worst first, host tiles, disk outlook, counts) and hourly totals, the Log Explorer the summary's `total`
and `errors`, and Settings the page's rule counts and `/api/v1/agents/summary`. A screen may still cut a complete
list to the room it has, or group one for layout, but it never filters, sorts, or counts the rows it got.
Simple shows the fundamentals; Advanced shows everything, the fundamentals included. The choice is one for the whole
app, kept in the browser, and Simple until someone picks Advanced. A filter that is in use always shows, so a shared
link never hides part of what it filters by. Screens that are basic already, such as Users, Account, Ingestion keys,
and Notifications, have no toggle.

| Screen   | Simple                                                                                                                                           | Advanced adds                                                                                             |
| -------- | ------------------------------------------------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------- |
| Logs     | time, severity, services; a record's message, stack, attributes                                                                                  | attribute and trace filters, Export, resource and trace                                                   |
| Traces   | time, what to show, services; a trace's spans and logs                                                                                           | duration and attribute filters, Attributes tab, span events                                               |
| Alerts   | headline, state, the list; an alert's facts and chart                                                                                            | weather log, rule and text filters, built-in rules, timeline, errors, disks, routes                       |
| Metrics  | cards per host, service, and database server; status, rings, the basic charts; a database server's numbers, databases, running queries, and jobs | metric browser, storage tiles, every panel; a server's page life, waits, growth, I/O latency, top queries |
| Settings | retention, rules' switches and levels, email server, sign-in                                                                                     | every other setting                                                                                       |

The Playwright mock starts each test in Advanced, so the existing specs see every part; `e2e/views.spec.ts` covers
Simple at every size.

## Layout

```
src/
  account/    Account page: password, second factor, recovery codes, sessions, sign-ins
  alerts/     alert details screen: chart, timeline, most frequent errors
  api/        REST client and the API's types
  app/        composition root: the route registry, the look contract and the looks, the shell contract, and what
              every look's shell shares (status, times, theme, drawer, alert counts, the environment switcher)
  auth/       sign-in and first-run setup
  home/       home screen: snapshot hook, Needs you, weather icons; forecast/ holds the Forecast visual,
              large/ the estate on every layout, and the panels and the wall of large screens (VEY-24)
  integrations/  integrations: the list, the wizard's rules, probes, the package page's hook, words, plain screens
  jobs/       jobs and pipelines: the list and one job, their hooks, the search and filters in the address, words
  keys/       ingestion keys screen
  looks/      the looks of the whole app (ADR 0021); console/ holds the Console shell: grouped sidebar, top bar,
              account menu, and its colour tokens; forecast/ the Forecast shell: sidebar, account menu, weather,
              phone bar and drawer; each with its menu
  time/       the time zone every screen shows times in, and zone-aware dates and days
  logs/       Log Explorer: filters, histogram, list, detail, and the search hook
  metrics/    Metrics overview, host and service screens, metric browser, and the line chart, rings, and sparklines
  notifications/  notification channels screen: cards and the side panel to add or change one
  overview/   the Console Overview: its figures, panels, and tables, from one read
  resources/  Services, Hosts & containers, and Databases lists: one frame, the figures, filters, and Load more
  session/    who is signed in, shared through React context
  settings/   Settings screen: sections, fields, review dialog, history panel (ADR 0017); Database monitoring with
              the panel that adds a SQL Server, and Agents (ADR 0019)
  styles/     design tokens for both themes, base styles
  test/       Vitest setup
  traces/     Traces screens: trace list with the duration chart, trace view with the waterfall and span panel
  twofactor/  shared two-factor parts: QR code, code field, setup steps, recovery codes
  ui/         pieces every look and feature uses: HelpTip, ScreenBar, ViewToggle, CommandBlock, Logo, NavIcon
  users/      Users screen: security strip, role bar, badges, person panel, add user, reset password
e2e/          Playwright tests, run at desktop, tablet, and phone sizes
scripts/      checks that ESLint does not cover, run by npm run lint
```

### Looks and layers

A look (ADR 0021) arranges the app: its shell, its menu, its home, and its own layout of a screen where it needs one.
It never fetches or works out data; features do, and every figure comes from the server. `app/routes.tsx` lists
every destination once, and a look's `nav.ts` only groups route ids, so URLs are the same in every look. A test
checks that every menu route appears exactly once in every look's menu.

Imports go one way: utilities (`time/`) ← API ← ui ← features ← looks ← app. A feature imports nothing from
`app/` but the shell contract (`app/shellContext.ts`), a look never imports another look or the API client, and
`npm run lint` fails on any import that breaks this (`scripts/check-boundaries.mjs`). To add a look, add a folder
under `looks/` with a shell, a menu, a home, and a stylesheet, and register it in `app/looks.ts`.

Components, styles, and tests sit side by side. Colours come from `styles/tokens.css`, except the weather scenes of
the home, which `home/home.css` defines for both themes.

Vite bundles every stylesheet into one, so a class styles every element that carries it, on every screen. Give a
screen's classes the screen's name, and put controls used across screens, such as buttons and switches, in
`styles/tokens.css`. `npm run lint` fails when a class one feature styles appears in another feature's stylesheet
(`scripts/check-css-owners.mjs`).

## Commands

Run from this folder.

```bash
npm ci
```

```bash
npm run dev
```

```bash
npm test
```

```bash
npm run test:e2e
```

```bash
npm run lint
```

```bash
npm run format:check
```

```bash
npm run build
```

- `npm run dev` serves the app on port 5173 and forwards `/api` to the API on its development port 5292
  (`dotnet run --project src/backend/Veyra.Api`).
- `npm test` runs the unit and component tests (Vitest, Testing Library, jsdom).
- `npm run test:e2e` builds the app and runs it in Chromium at three viewport sizes. The tests answer the REST API
  themselves, so no backend is needed. Run `npx playwright install chromium` once first.
- `npm run build` type-checks and writes `dist/`. The Docker image builds the app in its own stage and copies
  `dist/` into the API's `wwwroot`.
