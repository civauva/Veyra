using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

// A minimal web app that sends its logs to Veyra with the standard OpenTelemetry .NET SDK and nothing else.
// Where to send them is the SDK's usual environment configuration, for example:
//
//   OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
//   OTEL_EXPORTER_OTLP_HEADERS=Authorization=Bearer vyk_...
//   OTEL_RESOURCE_ATTRIBUTES=deployment.environment.name=development
//
// See docs/integrations/dotnet.md.
var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddOpenTelemetry(logging =>
{
    // Keep the rendered message as the body, so the Log Explorer shows readable text.
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("veyra-sample"));
    logging.AddOtlpExporter();
});

var app = builder.Build();

// Writes one information record per call.
app.MapGet("/", (ILogger<SampleEndpoints> logger) =>
{
    SampleLog.Greeted(logger, DateTimeOffset.UtcNow);

    return Results.Ok("Logged a greeting. Look for it in Veyra.");
});

// Writes one error record with an exception, to see how failures arrive.
app.MapGet("/fail", (ILogger<SampleEndpoints> logger) =>
{
    try
    {
        throw new InvalidOperationException("The sample failed on purpose.");
    }
    catch (InvalidOperationException exception)
    {
        SampleLog.Failed(logger, exception);
    }

    return Results.Ok("Logged an error. Look for it in Veyra.");
});

await app.RunAsync();

/// <summary>The category name of the sample's log records.</summary>
internal sealed class SampleEndpoints;

/// <summary>The sample's log messages, source-generated as Veyra's coding standards ask for.</summary>
internal static partial class SampleLog
{
    /// <summary>Logs a greeting.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="time">When the greeting happened.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Hello from the Veyra sample at {Time}")]
    public static partial void Greeted(ILogger logger, DateTimeOffset time);

    /// <summary>Logs a failure.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">What went wrong.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "The sample request failed")]
    public static partial void Failed(ILogger logger, Exception exception);
}
