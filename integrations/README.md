# Integrations

Versioned, tested OpenTelemetry Collector configurations for each monitored target. Each folder contains a
`collector.yaml` that exports to Veyra over OTLP, plus any platform notes. Documentation lives in `/docs/integrations`.

| Folder | Target |
| --- | --- |
| `azure/` | Azure resources via Azure Monitor and Event Hub receivers |
| [`linux/`](linux/collector.yaml) | Linux host metrics; journald and log files later |
| [`windows/`](windows/collector.yaml) | Windows host metrics and Event Log errors, IIS sites in `iis.yaml`; checked in CI by `test/run.ps1` |
| [`docker/`](docker/collector.yaml) | Docker container stats and logs, alone or beside `linux/`; checked in CI by `test/run.sh` |
| [`postgres/`](postgres/collector.yaml) | PostgreSQL 14 and newer: connections, sizes, deadlocks, locks, vacuum, wraparound, blocking, long queries; checked in CI by `test/run.sh` |
| [`sqlserver/`](sqlserver/collector.yaml) | SQL Server 2019, 2022, 2025: health, blocking, backups, SQL Agent jobs; checked in CI by `test/run.sh` |
| [`traces/`](traces/collector.yaml) | Tail sampling of traces between applications and Veyra |

`sqlserver/collector.yaml` is also the template Veyra's agents are configured from (ADR 0019): Veyra copies its
components for each server added on the Settings screen and fills in the values. A change to it reaches every agent,
so the template and its CI check stay the one contract for both ways of running it. Its variables are the names the
Agents module fills in; a new one needs a value there too. `windows/` and `linux/` are the templates of an agent that
watches its own host (VEY-35), merged in the same way, and `docker/` joins `linux/` when the agent reads its
containers (VEY-32). `postgres/collector.yaml` is the template for each PostgreSQL server added on the Settings screen
(VEY-33), copied per server like the SQL Server one; its shared processors and exporter must stay the same as
`sqlserver/`'s, since an agent that reads both keeps them once.
