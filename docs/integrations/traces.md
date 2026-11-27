# Traces

Veyra receives traces over OTLP from the standard OpenTelemetry SDKs and the Collector (ADR 0014). It stores every
span it receives and does not sample, so what it shows is exactly what was sent. That makes sampling the sender's
choice, and the first one to make: traces are the largest signal by far.

## How much to keep

An API at 100 requests a second sends about 70 million spans a day, 35 to 70 GB before sampling. Veyra keeps traces
7 days by default (`VEYRA_TRACE_RETENTION_DAYS`) and can guard the disk with a budget
(`VEYRA_MAX_TRACE_STORAGE_GB`), but both only drop what was already stored. Decide the volume before Veyra:

- **In the SDK (head sampling):** `OTEL_TRACES_SAMPLER=parentbased_traceidratio` and `OTEL_TRACES_SAMPLER_ARG=0.1`
  keep a fixed share of all traces, decided when each starts.
- **In the Collector (tail sampling):** [`integrations/traces/collector.yaml`](../../integrations/traces/collector.yaml)
  keeps every failed trace, every slow one, and a share of the rest, decided once each trace has ended.

Tail sampling keeps what people open: every trace with a failed span and every trace slower than `SLOW_MS`, plus
`KEEP_PERCENT` of the rest, chosen by trace ID so every span of a kept trace is kept. Tested against Veyra: of 701
traces sent, all 55 failed and all 17 slow ones arrived, and 73 of the other 629, about the 10% asked for.

Request rates, error rates, and latency on the home, the Metrics screens, and the alert rules come from metrics,
which are not sampled, so they stay right however traces are sampled.

## From an application

The SDKs send traces with the same endpoint, key, and environment as logs and metrics. For .NET, add tracing to the
setup in [`dotnet.md`](dotnet.md):

```csharp
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("payments-api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()   // a server span for each request
        .AddHttpClientInstrumentation()   // a client span for each outgoing call, which carries the trace on
        .AddOtlpExporter());
```

with the packages `OpenTelemetry.Instrumentation.AspNetCore` and `OpenTelemetry.Instrumentation.Http`. Database
clients add their own spans, for example `Npgsql.OpenTelemetry` (`.AddNpgsql()`) or
`OpenTelemetry.Instrumentation.SqlClient`.

Without a Collector, point `OTEL_EXPORTER_OTLP_ENDPOINT` at Veyra and sample in the SDK. With the Collector above, point
it at the Collector and leave the SDK's sampler at its default, so the Collector sees every span.

## Running the Collector

```bash
docker run -d --name veyra-traces \
  -p 4317:4317 -p 4318:4318 \
  -e VEYRA_OTLP_ENDPOINT=https://veyra.example.com \
  -e VEYRA_INGESTION_KEY=vyk_... \
  -e KEEP_PERCENT=10 -e SLOW_MS=1000 \
  -v "$PWD/integrations/traces:/etc/veyra" \
  otel/opentelemetry-collector-contrib:0.140.0 --config /etc/veyra/collector.yaml
```

Tail sampling decides a trace once it has waited 10 seconds for its last span, so traces appear in Veyra about 10
seconds after they end. It needs every span of a trace in one Collector: with several, put the `loadbalancing`
exporter in front, routing by trace ID.

## In Veyra

- **Traces** in the sidebar lists traces from their root spans, with a chart of every trace's duration. Drag across
  the chart to pick traces by time and duration; click a dot or a row to open a trace.
- A trace opens on a waterfall with the critical path, the span that caused the failure beside it, and the trace's
  log records. A span whose parent has not arrived is marked, because a trace is never sure to be complete.
- A log record with a trace ID opens its trace from the Log Explorer, and the trace view links back to its records.
- A service's error-rate alert links to its failed traces of the alert's window, and a latency alert to its slow ones.
- A root span that names a job, such as `job.name`, is also a run of that job on the Jobs screens, kept 30 days; see
  [`jobs.md`](jobs.md).
