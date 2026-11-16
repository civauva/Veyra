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
| `/keys`                              | Admin                                        | Ingestion keys: create (the secret is shown once), list, revoke                                                  |
| `/users`                             | Admin                                        | Users: security rings, second-factor rule, person badges, and a panel per person                                 |
| `/settings?section=…`                | Admin                                        | Settings: every module's settings, review, history, export and import; Database monitoring and Agents (ADR 0019) |
| `/account`                           | Viewer                                       | Your account: password, second factor, recovery codes, sessions, sign-ins                                        |
| `/notifications`                     | Admin                                        | Webhook and email channels: add, change, switch off, test, delete                                                |

The trace list asks for the page, the duration chart, and the services together, for one window
(`traces/useTraceList.ts`), and looks again every 30 seconds while the window ends now. The trace view reads the trace
every 30 seconds too, because spans of other services may still be arriving, and says when part of it is missing
(ADR 0014).

The Log Explorer asks for the records and the histogram's counts together and shows each as it arrives
(`logs/useLogSearch.ts`): the list never waits for the counts, and the histogram says it is counting until they come.

The shell asks for the home snapshot every 15 seconds on every screen (`app/useShellStatus.ts`) and keeps the last
picture when a refresh fails. It draws the sidebar of every screen (`app/Sidebar.tsx`, ADR 0013) with the environment
switcher and the open alerts, and the weather behind it (`app/Weather.tsx`): full on the home and alert screens, a
calm band on work screens. The sidebar is labelled from 1600 px, icons from 1024 px, and a drawer below that
(`app/useSidebar.ts`); collapsing or expanding it is kept in this browser. Until a
person picks an environment, production is shown when there is one, otherwise all environments; the choice is kept in
this browser, and the home, Alerts, Logs, and Metrics follow it. The shell's navigation links move between screens
through a view transition (`app/useBlendedNavigation.ts`). "Open logs" and "± 1 minute" on an alert lead into the Log Explorer, "Details" to the alert's own screen.

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

Times and days are shown in each person's zone (VEY-22, `time/zone.ts`): the one kept on the account from the
Account page's Times section, or the browser's until they pick one. The account menu switches every screen to UTC in
this browser. Column heads and captions name the zone, hovering a log record's time shows it in UTC, and copied
records and exports keep UTC instants. The alert summary's days start at the person's midnight.

Every screen works on desktop, tablet, and phone, in light and dark themes, and with the keyboard. In the Log
Explorer the arrow keys move the selection and Escape closes the record or the filter sheet.

### Simple and Advanced

Settings and the monitoring screens have a **Simple / Advanced** toggle in their header (`app/ViewToggle.tsx`).
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
  app/        app frame: routes, sidebar with the environment switcher and account menu, phone bar and drawer
  auth/       sign-in and first-run setup
  home/       home screen: snapshot hook, Needs you, weather icons; forecast/ holds the Forecast visual,
              large/ the estate on every layout, and the panels and the wall of large screens (VEY-24)
  keys/       ingestion keys screen
  time/       the time zone every screen shows times in, and zone-aware dates and days
  logs/       Log Explorer: filters, histogram, list, detail, and the search hook
  metrics/    Metrics overview, host and service screens, metric browser, and the line chart, rings, and sparklines
  notifications/  notification channels screen: cards and the side panel to add or change one
  session/    who is signed in, shared through React context
  settings/   Settings screen: sections, fields, review dialog, history panel (ADR 0017); Database monitoring with
              the panel that adds a SQL Server, and Agents (ADR 0019)
  styles/     design tokens for both themes, base styles
  test/       Vitest setup
  traces/     Traces screens: trace list with the duration chart, trace view with the waterfall and span panel
  twofactor/  shared two-factor parts: QR code, code field, setup steps, recovery codes
  users/      Users screen: security strip, role bar, badges, person panel, add user, reset password
e2e/          Playwright tests, run at desktop, tablet, and phone sizes
scripts/      checks that ESLint does not cover, run by npm run lint
```

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
