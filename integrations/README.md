# Integrations

Versioned, tested OpenTelemetry Collector configurations for each monitored target. Each folder contains a
`collector.yaml` that exports to Veyra over OTLP, plus any platform notes. Documentation lives in `/docs/integrations`.

| Folder | Target |
| --- | --- |
| `azure/` | Azure resources via Azure Monitor and Event Hub receivers |
| [`linux/`](linux/collector.yaml) | Linux host metrics; journald and log files later |
| `docker/` | Docker container stats and logs |
| `postgres/` | PostgreSQL health and performance |
| [`sqlserver/`](sqlserver/collector.yaml) | SQL Server 2019, 2022, 2025: health, blocking, backups, SQL Agent jobs; checked in CI by `test/run.sh` |
| [`traces/`](traces/collector.yaml) | Tail sampling of traces between applications and Veyra |

`sqlserver/collector.yaml` is also the template Veyra's agents are configured from (ADR 0019): Veyra copies its
components for each server added on the Settings screen and fills in the values. A change to it reaches every agent,
so the template and its CI check stay the one contract for both ways of running it. Its variables are the names the
Agents module fills in; a new one needs a value there too.
