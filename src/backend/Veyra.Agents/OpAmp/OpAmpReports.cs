using OpAmp.Proto.V1;
using Veyra.Agents.Agents;

namespace Veyra.Agents.OpAmp;

/// <summary>
/// Merges what an agent sends over OpAMP into what Veyra knows of it. OpAMP leaves out every part that has not
/// changed, so a missing part keeps its last value.
/// </summary>
internal static class OpAmpReports
{
    /// <summary>Merges one message into the last report.</summary>
    /// <param name="known">What Veyra knew before.</param>
    /// <param name="message">What the agent sent now.</param>
    /// <param name="now">When it arrived.</param>
    /// <returns>The new report.</returns>
    public static AgentReport Merge(AgentReport known, AgentToServer message, DateTimeOffset now)
    {
        var report = known with
        {
            InstanceUid = message.InstanceUid.IsEmpty ? known.InstanceUid : message.InstanceUid.ToByteArray(),
            LastSeenAt = now,
        };

        if (message.AgentDescription is { } description)
        {
            var identifying = Attributes(description.IdentifyingAttributes);
            var other = Attributes(description.NonIdentifyingAttributes);

            report = report with
            {
                Version = identifying.GetValueOrDefault("service.version"),
                HostName = other.GetValueOrDefault("host.name"),
                OperatingSystem = other.GetValueOrDefault("os.description") ?? other.GetValueOrDefault("os.type"),
            };
        }

        if (message.Health is { } health)
        {
            report = report with
            {
                Healthy = health.Healthy,
                HealthError = health.Healthy ? null : FirstError(health),

                // OpAMP gives the time in nanoseconds since 1970; 0 means the agent did not say.
                StartedAt = health.StartTimeUnixNano > 0
                    ? DateTimeOffset.UnixEpoch.AddTicks((long)(health.StartTimeUnixNano / 100))
                    : report.StartedAt,
            };
        }

        if (message.RemoteConfigStatus is { } status)
        {
            report = report with
            {
                ConfigHash = status.LastRemoteConfigHash.IsEmpty ? null : status.LastRemoteConfigHash.ToByteArray(),
                ConfigState = Enum.IsDefined((AgentConfigState)status.Status)
                    ? (AgentConfigState)status.Status
                    : AgentConfigState.Unknown,
                ConfigError = string.IsNullOrEmpty(status.ErrorMessage) ? null : status.ErrorMessage,
            };
        }

        return report;
    }

    private static Dictionary<string, string> Attributes(IEnumerable<KeyValue> attributes) =>
        attributes
            .Where(attribute => attribute.Value?.ValueCase == AnyValue.ValueOneofCase.StringValue)
            .GroupBy(attribute => attribute.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value.StringValue, StringComparer.Ordinal);

    // The Collector reports health per pipeline and per component. The first error found, outermost first, says most.
    private static string? FirstError(ComponentHealth health) =>
        !string.IsNullOrEmpty(health.LastError)
            ? health.LastError
            : health.ComponentHealthMap
                .OrderBy(part => part.Key, StringComparer.Ordinal)
                .Select(part => FirstError(part.Value))
                .FirstOrDefault(error => error is not null);
}
