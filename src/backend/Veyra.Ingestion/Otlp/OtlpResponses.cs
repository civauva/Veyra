using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using Veyra.Ingestion.Pipeline;

namespace Veyra.Ingestion.Otlp;

/// <summary>Builds OTLP export responses, with partial success when something was refused.</summary>
internal static class OtlpResponses
{
    /// <summary>The answer to a logs export.</summary>
    /// <param name="result">What the pipeline did.</param>
    /// <returns>An empty response, or partial success with the count and the reason.</returns>
    public static ExportLogsServiceResponse Logs(IngestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var response = new ExportLogsServiceResponse();

        if (result.Rejected > 0)
        {
            response.PartialSuccess = new ExportLogsPartialSuccess
            {
                RejectedLogRecords = result.Rejected,
                ErrorMessage = result.RejectionReason ?? string.Empty,
            };
        }

        return response;
    }

    /// <summary>The answer to a metrics export.</summary>
    /// <param name="result">What the pipeline did.</param>
    /// <returns>An empty response, or partial success with the count and the reason.</returns>
    public static ExportMetricsServiceResponse Metrics(IngestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var response = new ExportMetricsServiceResponse();

        if (result.Rejected > 0)
        {
            response.PartialSuccess = new ExportMetricsPartialSuccess
            {
                RejectedDataPoints = result.Rejected,
                ErrorMessage = result.RejectionReason ?? string.Empty,
            };
        }

        return response;
    }
}
