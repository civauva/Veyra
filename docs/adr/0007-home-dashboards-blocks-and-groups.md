# 0007. Home dashboards on a shared block and group model

Date: 2026-11-08
Status: Accepted

## Context

The home screen must show the important metrics, warnings, and errors at first sight, with few details. Design
exploration produced two homes worth building, and more are likely:

- **Forecast** reads the environment like a weather report: one condition, one number, the next hours, the outlook.
- **City** draws every monitored system as a building, grouped into districts.

Different people want different homes, so Veyra should offer several and let each user pick one. That only works
if every home shows the same facts. A system that is on fire in one home cannot be missing, healthy, or called
something else in another. People also need to arrange systems in their own way, for example by layer, team, or
user journey. And a set of many similar systems, such as four Kubernetes nodes, must be able to show as one item.

Nothing in Veyra models "a monitored system" yet. Telemetry arrives over OTLP with resource attributes such as
`service.name`, `host.name`, and `db.system`, and nothing above that level exists.

## Decision

Build every home dashboard on one shared model of **blocks** and **groups**, served as one read model. Each
dashboard is a frontend module that only decides how to draw that model.

1. **Block.** A block is one monitored thing: a service, host, container, Kubernetes node, database, queue, job,
   cloud resource, or external dependency. Its stable identity comes from OTLP resource attributes, so no
   Veyra-specific SDK is needed. Every block has the same contract: identity, kind, name, environment, up to four
   standard signals (traffic, errors, latency, saturation), one health state with a reason, a headline value, a
   size weight, and its dependency links.
2. **Health comes from alerting only.** A block's health is one of `ok`, `caution`, `warning`, or `unknown`, and it
   is set by alert rules in `Veyra.Alerting`. Dashboards never apply their own thresholds. `unknown` means no data
   arrived in the expected interval. It is never shown as healthy.
3. **Grouping.** A grouping is a named way to split all blocks of an environment into groups, for example Layer
   (built in, by kind), Team, Journey, or any resource attribute. Within one grouping, each block belongs to
   exactly one group. Rules are matched in order, manual includes and excludes are allowed, and blocks that match
   no rule go to a visible `Ungrouped` group. Groups may contain subgroups, at most two levels deep.
4. **Groups are blocks.** A group can be shown collapsed, and then it obeys the full block contract: health is the
   worst of its members, the weight is the sum of their traffic, and the headline names its worst member and how
   many members are affected. Every dashboard can draw a collapsed group anywhere it can draw a block.
5. **Decent-citizen rules.** Every dashboard must obey them:
   - Every block in the environment is drawn or counted. If space runs out, the dashboard collapses groups or
     shows a visible "+N more", and never drops blocks silently.
   - States, colours, and words mean the same thing in every dashboard.
   - A block looks the way its data says: kind, weight, health, and activity. A dashboard has no styling of its
     own per block.
   - Every block and group can be selected. The selection is shared with the shell, and selecting opens the same
     detail drawer everywhere.
   - Positions are stable between refreshes. New blocks are added to their group without reshuffling the rest.
6. **Shell and dashboard split.** The shell owns everything that must look the same on every home:
   - the top bar: environments, home choice, master warning and caution, and the clock
   - the Needs you pane, with alerts worst first, numbered, and the steps for the selected alert
   - the detail drawer, the selection, and the refresh

   A dashboard owns only its main visual and an optional pane footer, such as Forecast's system rows or City's
   districts.
7. **One read model.** `GET /api/v1/home?environment=…` returns the home snapshot: the environment condition, the
   ordered alerts, the blocks, the groupings, and the hourly and outlook series. All dashboards use the same
   snapshot, so a new dashboard needs no backend change.
8. **Ownership.**
   - `Veyra.Core` defines the shared types and the interfaces between modules.
   - `Veyra.Ingestion` reports the resources it sees through a Core interface.
   - `Veyra.Query` owns the block catalog, groupings, layout pins, and the snapshot, with its own schema objects.
   - `Veyra.Alerting` supplies health through a Core interface.
9. **Choice.** The first dashboards are Forecast, which is the instance default, and City. Each user can choose
   their home, and that choice is stored as a user preference. The instance default is set in configuration.
   Station stays a separate view and is not a home.

The block model, the grouping rules, and how each dashboard maps them are specified in
[`docs/architecture/home-dashboards.md`](../architecture/home-dashboards.md).

## Alternatives considered

- **One fixed Overview screen.** This is simpler, but design rounds showed that no single home suits everyone, and
  adding a second home later would need a new data contract anyway.
- **Each dashboard queries what it needs.** This is flexible, but each dashboard would decide health its own way,
  and the same system could look different from one home to another. It would also put backend work into every
  new dashboard.
- **Configurable tile grid as the home.** This is the familiar dashboard model, but it moves the choice of what
  matters to the user every time and fails the "important things at first sight" requirement. Tiles may come
  later as a view, not as the home.
- **Health computed in the frontend from raw signals.** This needs no alerting dependency, but it creates a second
  set of thresholds that drift from alert rules, and a lit block with no alert behind it cannot be explained.
- **A separate `Catalog` module.** It would be a clean owner for blocks and groupings, but it adds a module before
  the catalog is big enough to need one. Revisit this when topology or ownership data grows.
- **Runtime-loaded dashboard plugins.** This allows third-party homes, but it is a security and versioning burden
  before the contract is stable. Dashboards are compiled into the frontend for now.

## Consequences

- **Easier.** A new home dashboard is frontend-only work against a fixed snapshot. Groupings defined once serve
  every dashboard, and later alert rules and views can use them too.
- **Harder.** Block identity has to be derived reliably from resource attributes. A renamed service becomes a new
  block, so the catalog needs aliases and merges. Unknown state needs an expected interval for each kind.
- **Scale.** The snapshot must stay small. The target is under 1 MB for 2,000 blocks, which relies on collapsed
  groups. Each dashboard declares how many items it can draw, and the shell collapses groups to fit.
- **Early versions.** In v0.1, only logs exist. Blocks then carry log-derived signals, with log rate as traffic and
  error-log rate as errors, and the other signals are absent. Dashboards must render missing signals without gaps
  or errors.
- **Actions.** Buttons such as "Roll back" are links or webhooks configured on alert rules. Veyra does not deploy
  anything itself.
- **Tests.** Each dashboard has contract tests against shared fixture snapshots: storm, calm, empty, all unknown,
  and 2,000 blocks. Each fixture runs in light and dark themes and with keyboard-only navigation.
