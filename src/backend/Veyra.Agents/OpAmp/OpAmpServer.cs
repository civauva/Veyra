using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using OpAmp.Proto.V1;
using Veyra.Agents.Agents;
using Veyra.Agents.Collector;
using Veyra.Agents.PostgresServers;
using Veyra.Agents.SqlServers;
using Veyra.Core.Security;

namespace Veyra.Agents.OpAmp;

/// <summary>
/// Veyra's side of OpAMP (ADR 0019): it answers each agent's poll with the agent's whole Collector configuration and
/// the address for its own logs, and keeps what the agent reports.
/// </summary>
/// <remarks>
/// <para>
/// The answer carries the configuration every time, with its hash. The supervisor applies it only when the hash
/// differs from what it runs, so a poll that changes nothing restarts nothing. This keeps Veyra free of per-agent
/// state about what was sent.
/// </para>
/// <para>
/// The agent's key arrives with every poll, so Veyra can write it into the configuration without storing it: only its
/// hash is kept, by the Access module.
/// </para>
/// </remarks>
/// <param name="agents">Where agents and their reports are stored.</param>
/// <param name="servers">The SQL Servers each agent reads.</param>
/// <param name="postgres">The PostgreSQL servers each agent reads (VEY-33).</param>
/// <param name="keys">Checks the key each poll is signed with.</param>
/// <param name="clock">The clock for the time an agent was last seen.</param>
internal sealed class OpAmpServer(
    AgentStore agents,
    SqlServerService servers,
    PostgresServerService postgres,
    IIngestionKeyValidator keys,
    TimeProvider clock)
{
    private const ulong _capabilities =
        (ulong)ServerCapabilities.AcceptsStatus
        | (ulong)ServerCapabilities.OffersRemoteConfig
        | (ulong)ServerCapabilities.AcceptsEffectiveConfig
        | (ulong)ServerCapabilities.OffersConnectionSettings;

    /// <summary>Handles one poll of an agent.</summary>
    /// <param name="key">The key from the poll's <c>Authorization: Bearer</c> header, if it had one.</param>
    /// <param name="message">What the agent sent.</param>
    /// <param name="veyraUrl">Veyra's address as the agent reached it, where its Collector sends what it reads.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The answer, or <see langword="null"/> when the key is missing, invalid, or belongs to no active agent.
    /// </returns>
    public async Task<ServerToAgent?> HandleAsync(
        string? key,
        AgentToServer message,
        Uri veyraUrl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(veyraUrl);

        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (await keys.ValidateAsync(key, cancellationToken) is not { IsValid: true } check)
        {
            return null;
        }

        if (await agents.FindByKeyAsync(check.KeyId!.Value, cancellationToken) is not { } agent)
        {
            return null;
        }

        var report = OpAmpReports.Merge(agent.Report, message, clock.GetUtcNow());
        await agents.SaveReportAsync(agent.Id, report, cancellationToken);

        var targets = await servers.TargetsAsync(agent.Id, cancellationToken);
        var postgresTargets = await postgres.TargetsAsync(agent.Id, cancellationToken);
        var connection = new AgentConnection(veyraUrl, key);
        var config = Encoding.UTF8.GetBytes(
            CollectorConfig.Build(connection, targets, HostReadsOf(agent), postgresTargets));

        return new ServerToAgent
        {
            InstanceUid = message.InstanceUid,
            Capabilities = _capabilities,
            Flags = NeedsFullState(agent.Report, message) ? (ulong)ServerToAgentFlags.ReportFullState : 0,
            RemoteConfig = new AgentRemoteConfig
            {
                Config = new AgentConfigMap
                {
                    ConfigMap =
                    {
                        [""] = new AgentConfigObject
                        {
                            Body = ByteString.CopyFrom(config),
                            ContentType = "text/yaml",
                        },
                    },
                },
                ConfigHash = ByteString.CopyFrom(SHA256.HashData(config)),
            },
            ConnectionSettings = OwnLogs(veyraUrl, key),
        };
    }

    // The host is read only when an Admin switched it on; the Event Log and IIS only count on a Windows agent, and
    // Docker only on a Linux one.
    private static HostReads? HostReadsOf(Agent agent) =>
        agent.Watch.Host
            ? new HostReads(
                agent.Report.IsWindows,
                agent.Watch.EventLog,
                agent.Watch.Iis,
                agent.Watch.Environment,
                agent.Watch.Docker)
            : null;

    // An agent that starts afresh, or a Veyra that lost what it knew, gets the agent to repeat everything: OpAMP
    // otherwise sends only what changed since the last poll.
    private static bool NeedsFullState(AgentReport known, AgentToServer message) =>
        message.AgentDescription is null
        && (known.Version is null
            || known.InstanceUid is null
            || !message.InstanceUid.Span.SequenceEqual(known.InstanceUid));

    // The agent sends its Collector's own logs to Veyra like any telemetry. They are how a failed login or a missing
    // grant reaches the Settings screen: the Collector's health stays good through both.
    private static ConnectionSettingsOffers OwnLogs(Uri veyraUrl, string key)
    {
        var endpoint = new Uri(veyraUrl, "/v1/logs").ToString();

        return new ConnectionSettingsOffers
        {
            Hash = ByteString.CopyFrom(SHA256.HashData(Encoding.UTF8.GetBytes($"{endpoint}\n{key}"))),
            OwnLogs = new TelemetryConnectionSettings
            {
                DestinationEndpoint = endpoint,
                Headers = new Headers { Headers_ = { new Header { Key = "Authorization", Value = $"Bearer {key}" } } },
            },
        };
    }
}
