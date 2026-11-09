# .NET applications

A .NET application sends its logs to Veyra with the standard OpenTelemetry .NET SDK. No Veyra package is needed.
The sample in [`samples/Veyra.Sample.Api`](../../samples/Veyra.Sample.Api/Program.cs) does exactly this.

## 1. Add the SDK

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

## 2. Send logs over OTLP

```csharp
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;   // the rendered message becomes the body Veyra shows
    logging.IncludeScopes = true;
    logging.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("payments-api"));
    logging.AddOtlpExporter();
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

## What arrives

Each log call becomes one record with its message, severity, category (`otel.scope.name`), structured parameters
as attributes, and, for logged exceptions, `exception.type`, `exception.message`, and `exception.stacktrace`. Inside
a request, the record also carries the trace and span IDs, so it can be linked to the trace once traces exist.
