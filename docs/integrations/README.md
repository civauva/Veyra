# Integrations

Veyra receives telemetry over OTLP only. Collection from hosts, containers, cloud platforms, and databases is done by the
OpenTelemetry Collector using its existing receivers. Each page here documents one target and pairs with a tested collector
configuration in `/integrations/<target>/`.

| Target | Collector receiver(s) | Page |
| --- | --- | --- |
| Azure | `azuremonitor`, `azureeventhub` | `azure.md` |
| Linux hosts | `hostmetrics`, `filelog`, `journald` | `linux.md` |
| Docker containers | `docker_stats`, `filelog` | `docker.md` |
| PostgreSQL | `postgresql` | `postgres.md` |
| SQL Server | `sqlserver` | `sqlserver.md` |
| .NET applications | OpenTelemetry .NET SDK, OTLP exporter | [`dotnet.md`](dotnet.md) |
