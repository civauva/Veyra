using OpenTelemetry.Proto.Collector.Logs.V1;
using Veyra.Core.Security;
using Veyra.Core.Telemetry;
using Veyra.Ingestion.Otlp;

namespace Veyra.Ingestion.Pipeline;

/// <summary>
/// The part of ingestion that both OTLP transports share: map the request, apply the key's environment limit, and
/// queue the result.
/// </summary>
/// <param name="queue">The bounded queue to the database writer.</param>
/// <param name="clock">The clock for records that carry no time.</param>
internal sealed class LogPipeline(LogQueue queue, TimeProvider clock)
{
    /// <summary>
    /// Accepts the records of one request from a sender whose key was already checked.
    /// </summary>
    /// <param name="request">The decoded OTLP request.</param>
    /// <param name="key">The result of checking the sender's ingestion key. It must be valid.</param>
    /// <returns>How many records were queued or refused, or that Veyra is too busy right now.</returns>
    public IngestResult Accept(ExportLogsServiceRequest request, IngestionKeyCheck key)
    {
        var records = OtlpLogMapper.Map(request, clock.GetUtcNow());
        var accepted = records;
        string? reason = null;

        if (key.AllowedEnvironments is { } allowed)
        {
            accepted = new List<LogRecord>(records.Count);
            var refused = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var record in records)
            {
                if (record.Environment is not null && allowed.Contains(record.Environment))
                {
                    accepted.Add(record);
                }
                else
                {
                    refused.Add(record.Environment ?? "(none)");
                }
            }

            if (refused.Count > 0)
            {
                reason = $"This ingestion key may not send data for environment {string.Join(", ", refused)}.";
            }
        }

        if (!queue.TryEnqueue(accepted))
        {
            return new IngestResult(0, 0, null, QueueFull: true);
        }

        return new IngestResult(accepted.Count, records.Count - accepted.Count, reason, QueueFull: false);
    }
}
