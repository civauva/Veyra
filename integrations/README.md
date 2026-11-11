# Integrations

Versioned, tested OpenTelemetry Collector configurations for each monitored target. Each folder contains a
`collector.yaml` that exports to Veyra over OTLP, plus any platform notes. Documentation lives in `/docs/integrations`.

| Folder | Target |
| --- | --- |
| `azure/` | Azure resources via Azure Monitor and Event Hub receivers |
| [`linux/`](linux/collector.yaml) | Linux host metrics; journald and log files later |
| `docker/` | Docker container stats and logs |
| `postgres/` | PostgreSQL health and performance |
| `sqlserver/` | SQL Server health and performance |
