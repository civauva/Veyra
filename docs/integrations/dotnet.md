# .NET applications

A .NET application sends its logs and metrics to Veyra with the standard OpenTelemetry .NET SDK. No Veyra package is needed.
The sample in [`samples/Veyra.Sample.Api`](../../samples/Veyra.Sample.Api/Program.cs) does exactly this.

## 1. Add the SDK

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

## 2. Send logs and metrics over OTLP

```csharp
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("payments-api"))
    .WithLogging(
        logging => logging.AddOtlpExporter(),
        options =>
        {
            options.IncludeFormattedMessage = true;   // the rendered message becomes the body Veyra shows
            options.IncludeScopes = true;
        })
    .WithMetrics(metrics =>
    {
        // Built into .NET 9 and later: request durations and the runtime's CPU, memory, GC, and thread pool.
        metrics.AddMeter("Microsoft.AspNetCore.Hosting", "System.Runtime");
        metrics.AddOtlpExporter();
    });
```

## 3. Point it at Veyra

Create an ingestion key in Veyra (`POST /api/v1/ingestion-keys`, Admin), then configure the application through the
SDK's standard environment variables:

```bash
OTEL_EXPORTER_OTLP_ENDPOINT=https://veyra.example.com
OTEL_EXPORTER_OTLP_HEADERS=Authorization=Bearer%20vyk_...
OTEL_RESOURCE_ATTRIBUTES=deployment.environment.name=production
```

- **Endpoint.** `https://<host>` uses gRPC through Caddy. For OTLP/HTTP, also set
  `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`.
- **Header.** The space after `Bearer` is written `%20`, as the OpenTelemetry specification asks.
- **Environment.** Set `deployment.environment.name`; Veyra groups and filters by it, and keys can be limited to it.
- **Metric interval.** The SDK exports metrics every 60 seconds. `OTEL_METRIC_EXPORT_INTERVAL=30000` halves that, so
  the newest minute on the Metrics screens is filled sooner.

## What arrives

### Logs

Each log call becomes one record with its message, severity, category (`otel.scope.name`), structured parameters
as attributes, and, for logged exceptions, `exception.type`, `exception.message`, and `exception.stacktrace`. Inside
a request, the record also carries the trace and span IDs, so it can be linked to the trace once traces exist.

### Metrics

The service appears on the **Metrics** page with a card of its own, and on the Forecast home in the p95 latency
tile.

| Veyra shows | From |
| --- | --- |
| Requests per second, 4xx and 5xx, p50, p95, p99, routes | `http.server.request.duration` (`Microsoft.AspNetCore.Hosting`) |
| CPU | `dotnet.process.cpu.time` and `dotnet.process.cpu.count` (`System.Runtime`) |
| Memory | `dotnet.process.memory.working_set` |
| Garbage collections, thread pool queue, threads | `dotnet.gc.collections`, `dotnet.thread_pool.queue.length`, `dotnet.thread_pool.thread.count` |

- **Percentiles** are estimated from the histogram's buckets, the way Prometheus does it. ASP.NET Core's default
  buckets reach 10 seconds; slower requests count as 10 seconds.
- **Instances.** Run several instances with the same `service.name`; Veyra adds them up and lists them by
  `service.instance.id` or `host.name`.
- **Not stored.** Exponential histograms (`OTEL_EXPORTER_OTLP_METRICS_DEFAULT_HISTOGRAM_AGGREGATION`) and delta
  temporality for histograms are refused; keep the SDK defaults.
