# Changelog

Every release of Veyra, newest first. The app shows these notes behind its version, and offers to reload when a newer
version is running. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the versions
follow [Semantic Versioning](https://semver.org/): each release has a heading `## [version] - date`, an optional
sentence, and its changes under Added, Changed, Fixed, or Removed, one line a change. See
`docs/guides/releasing.md`.

## [Unreleased]

## [0.5.0] - 2026-11-27

Everything you see leads somewhere: details behind every tile, a fix behind every warning, and a lens for how much
each screen shows.

### Added

- Every figure, chart, row, and status pill with more behind it opens it: the resource lists, a SQL Server's tabs,
  Incidents, Traces, Metrics, and Alerts.
- Every warning leads to what it means and how to fix it: an alert opens at its fix, and a warning without an alert,
  such as a late backup, opens its rule's page with the steps and the open alerts.
- Links to a fix, or to a part of a screen, scroll to it and pulse its border, so you see where the click took you.
- Hovering a chart shows the moment under the pointer with each line's or bar's value; the arrow keys do the same.
- Add a server, watch a host, or connect a service from the lists that show them, for Admins.

### Changed

- Simple and Advanced is a lens in the top bar that says what each view shows, and every monitoring screen follows
  it: Simple keeps to what needs you, Advanced adds the plumbing, the extra columns, and the counters.
- The Alerts page puts its figures and the alerts per day in one compact band.
- Trails follow the menu: a host, a service, or a database server sits under its list, and a server's tab ends its
  trail.

### Fixed

- Monitoring settings on a database server's screen opens the database section of Settings.

## [0.4.1] - 2026-11-27

The Console look and its Overview, incidents, job runs, the SQL Server wizard, and release notes in the app.

### Added

- Console, a dense operations look, is the default; each person can pick Console or Forecast.
- The Overview: figures against the period before, what needs attention, request activity, telemetry arriving, the
  applications, hosts, databases, and jobs worst first, host resources, and the collectors.
- Tiles open their details: every figure, chart, and row of the Overview, and the figures on Alerts, Jobs, and a
  SQL Server's overview.
- One time range in the top bar for every screen, live updates, and a search across logs, traces, and resources.
- Incidents: alerts grouped by the blocks they touch, acknowledged by Editors, with runbook links and their context.
- Jobs and pipelines: runs recognised from traces and from SQL Server Agent, with their steps, kept 30 days.
- A SQL Server's overview, blocking chains, backup history against the policy, and storage growth.
- The SQL Server wizard: modules and intervals per server, a connection probe through the agent, and versioned
  templates.
- The log query language, saved views per person, levels, the host, and export from the server.
- The version in the sidebar opens these release notes, and a notice offers to reload when a newer version runs.

### Changed

- Every filter, sort, and count of every screen is worked out on the server.
- The Overview fills a large screen, and its lists show as many rows as fit.
- The open screen is marked in the sidebar by a band that runs to a lit edge.

### Fixed

- Collector health shows an online agent in green; it showed every state but a few words in red.
- Charts fill their tiles, and their labels and legends stay inside them.
- A database that shrank shows its growth as a negative size instead of raw bytes.
- The status lights in the sidebar's foot no longer touch their words.

## [0.2.1] - 2026-11-17

The first tagged release: logs, metrics, and traces over OTLP, alerts and notifications, SQL Server and host
monitoring through managed agents, and sign-in with roles.
