# Integrations

Veyra receives telemetry over OTLP only. Collection from hosts, containers, cloud platforms, and databases is done by the
OpenTelemetry Collector using its existing receivers. Each page here documents one target and pairs with a tested collector
configuration in `/integrations/<target>/`.

| Target | Collector receiver(s) | Page |
| --- | --- | --- |
| Azure | `azuremonitor`, `azureeventhub` | `azure.md` |
| Linux hosts | `hostmetrics`; later `filelog`, `journald` | [`linux.md`](linux.md) |
| Docker containers | `docker_stats`, `filelog` | `docker.md` |
| PostgreSQL | `postgresql` | `postgres.md` |
| SQL Server | `sqlserver`, `sqlquery` | [`sqlserver.md`](sqlserver.md) |
| Veyra agents | the Collector and its OpAMP supervisor, configured by Veyra | [`agents.md`](agents.md) |
| .NET applications | OpenTelemetry .NET SDK, OTLP exporter | [`dotnet.md`](dotnet.md) |
| Traces and sampling | OTLP exporter, `tail_sampling` processor | [`traces.md`](traces.md) |
