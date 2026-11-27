using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Veyra.Agents.Agents;
using Veyra.Agents.SqlServers;
using Veyra.Core.Agents;

namespace Veyra.Agents.Probes;

/// <summary>
/// Runs connection probes through an agent (ADR 0028): Test connection before a server is saved, and Validate now on a
/// saved one.
/// </summary>
/// <remarks>
/// <para>
/// A probe is a receiver in the chosen agent's configuration, named after the probe's ID, which the agent picks up
/// with its next poll. Its answer comes back as a <c>sqlserver.probe</c> record; a failed connection shows in the
/// Collector's own error logs under the receiver's name, as a server's does. The receiver leaves the configuration
/// once the probe finishes, or 2 minutes after it started.
/// </para>
/// <para>
/// The password of a probe is encrypted while it runs and set to null as soon as it finishes. Its outcome is kept a
/// day, so a saved server's page can show its last validation.
/// </para>
/// </remarks>
/// <param name="probes">Where probes are stored.</param>
/// <param name="servers">The saved servers, for Validate now.</param>
/// <param name="agents">The agents that run probes.</param>
/// <param name="passwords">Encrypts passwords while a probe runs.</param>
/// <param name="signals">The agents' own reader errors.</param>
/// <param name="clock">The current time.</param>
/// <param name="logger">Records probes that fail to finish, without passwords.</param>
internal sealed partial class ProbeService(
    ProbeStore probes,
    SqlServerService servers,
    AgentStore agents,
    SqlServerPasswordProtector passwords,
    SqlServerSignals signals,
    TimeProvider clock,
    ILogger<ProbeService> logger)
{
    /// <summary>How long a probe may wait for its answer.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    /// <summary>How long a finished probe's outcome is kept.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(1);

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>Starts a probe.</summary>
    /// <param name="input">The connection to test.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The probe: pending, or already failed when no agent can run it now. <see langword="null"/> when
    /// <see cref="ProbeInput.ServerId"/> names no saved server.
    /// </returns>
    /// <exception cref="AgentsValidationException">The connection breaks a rule.</exception>
    public async Task<SqlProbe?> StartAsync(ProbeInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var now = clock.GetUtcNow();
        SqlServerTarget? saved = null;

        if (input.ServerId is { } serverId)
        {
            saved = await servers.FindTargetAsync(serverId, cancellationToken);

            if (saved is null)
            {
                return await servers.FindAsync(serverId, cancellationToken) is null
                    ? null
                    : throw new AgentsValidationException(
                        "The server's password can no longer be read. Enter it again on the server's settings.");
            }
        }

        var agentId = input.AgentId ?? saved?.Server.AgentId;
        var agent = agentId is { } id
            ? await agents.FindAsync(id, cancellationToken)
            : await agents.FindBuiltInAsync(cancellationToken);

        if (agent is null)
        {
            var failed = new SqlProbe(
                Guid.CreateVersion7(),
                null,
                saved?.Server.Id,
                Unchecked(input, saved),
                now,
                now,
                ProbeState.Failed,
                ProbeFailure.NoAgent,
                agentId is null
                    ? "Veyra has no built-in agent. Choose the agent that reads this server."
                    : "The chosen agent no longer exists. Choose another.",
                null);

            await probes.InsertAsync(failed, null, cancellationToken);

            return failed;
        }

        var (connection, password) = Connection(input, saved, agent.Report);

        var probe = new SqlProbe(
            Guid.CreateVersion7(),
            agent.Id,
            saved?.Server.Id,
            connection,
            now,
            null,
            ProbeState.Pending,
            null,
            null,
            null);

        if (Offline(agent, now) is { } why)
        {
            probe = probe with
            {
                FinishedAt = now,
                State = ProbeState.Failed,
                Failure = ProbeFailure.AgentOffline,
                Message = why,
            };

            await probes.InsertAsync(probe, null, cancellationToken);

            return probe;
        }

        await probes.InsertAsync(probe, passwords.Protect(password), cancellationToken);
        LogStarted(probe.Id, agent.Id, probe.ServerId);

        return probe;
    }

    /// <summary>Reads a probe, finishing it first when its connection failed or its time ran out.</summary>
    /// <param name="id">The probe's ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The probe, or <see langword="null"/> when there is none, or it was purged after a day.</returns>
    public async Task<SqlProbe?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await probes.FindAsync(id, cancellationToken) is not { } probe)
        {
            return null;
        }

        if (probe.State != ProbeState.Pending)
        {
            return probe;
        }

        var now = clock.GetUtcNow();
        await RefreshAsync([probe], now, cancellationToken);

        return await probes.FindAsync(id, cancellationToken);
    }

    /// <summary>Finds a saved server's latest probe, finishing it first when it is due.</summary>
    /// <param name="serverId">The server's ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The probe, or <see langword="null"/> when it had none in the last day.</returns>
    public async Task<SqlProbe?> LatestForServerAsync(Guid serverId, CancellationToken cancellationToken) =>
        await probes.LatestForServerAsync(serverId, cancellationToken) is { } probe
            ? await GetAsync(probe.Id, cancellationToken)
            : null;

    /// <summary>The probes an agent runs now, with their passwords, for its configuration.</summary>
    /// <param name="agentId">The agent's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>
    /// The pending probes younger than 2 minutes, oldest first. One whose password can no longer be decrypted is left
    /// out; it times out.
    /// </returns>
    public async Task<IReadOnlyList<ProbeTarget>> TargetsAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var pending = await probes.ListPendingAsync(agentId, clock.GetUtcNow() - Timeout, cancellationToken);

        return pending
            .Select(entry => (
                entry.Probe,
                Password: entry.ProtectedPassword is { } text ? passwords.Unprotect(text) : null))
            .Where(entry => entry.Password is not null)
            .Select(entry => new ProbeTarget(entry.Probe.Id, entry.Probe.Connection, entry.Password!))
            .ToList();
    }

    /// <summary>Finishes a probe with the answer its agent sent.</summary>
    /// <param name="report">The answer.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>Whether the answer finished a pending probe; a repeat, a stranger's, or a late one does not.</returns>
    public async Task<bool> CompleteAsync(AgentProbeReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (!report.Attributes.TryGetValue("probe", out var text)
            || !Guid.TryParseExact(text, "N", out var id)
            || await probes.FindAsync(id, cancellationToken) is not { State: ProbeState.Pending } probe)
        {
            return false;
        }

        // Only the agent the probe was given to may answer it: another sender's key would let it fake an answer.
        if (probe.AgentId is not { } agentId
            || await agents.FindAsync(agentId, cancellationToken) is not { } agent
            || agent.IngestionKeyId is null
            || agent.IngestionKeyId != report.KeyId)
        {
            LogStranger(id);

            return false;
        }

        var finished = await probes.FinishAsync(
            id,
            ProbeState.Succeeded,
            null,
            null,
            Result(report.Attributes),
            clock.GetUtcNow(),
            cancellationToken);

        if (finished)
        {
            LogSucceeded(id);
        }

        return finished;
    }

    /// <summary>
    /// Finishes every pending probe that is due and deletes the outcomes older than a day. The probe worker calls it
    /// every few seconds, so a probe nobody reads still loses its password on time.
    /// </summary>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    /// <returns>A task that completes when the sweep is done.</returns>
    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var pending = await probes.ListAllPendingAsync(cancellationToken);

        if (pending.Count > 0)
        {
            await RefreshAsync(pending, now, cancellationToken);
        }

        await probes.DeleteStartedBeforeAsync(now - KeptFor, cancellationToken);
    }

    // Finishes the probes whose connection failed, by the Collector's errors under their receiver's name, and those
    // whose time ran out.
    private async Task RefreshAsync(
        IReadOnlyList<SqlProbe> pending,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var errors = await signals.ReadErrorsAsync(now, cancellationToken);

        foreach (var probe in pending)
        {
            var latest = errors
                .Where(error => error.ServerId == probe.Id && error.At >= probe.StartedAt)
                .MaxBy(error => error.At);

            if (latest is not null)
            {
                await probes.FinishAsync(
                    probe.Id,
                    ProbeState.Failed,
                    FailureOf(latest.Message),
                    latest.Message,
                    null,
                    now,
                    cancellationToken);
                LogFailed(probe.Id, latest.Message);
            }
            else if (now - probe.StartedAt >= Timeout)
            {
                await probes.FinishAsync(
                    probe.Id,
                    ProbeState.Failed,
                    ProbeFailure.TimedOut,
                    "The agent did not answer within 2 minutes. Check that it is online and that its Collector runs.",
                    null,
                    now,
                    cancellationToken);
                LogTimedOut(probe.Id);
            }
        }
    }

    /// <summary>What a connection error of a probe means.</summary>
    /// <param name="message">The error as the Collector logged it.</param>
    /// <returns>The failure.</returns>
    internal static ProbeFailure FailureOf(string message) =>
        SqlServerStatuses.ReasonOf(message) switch
        {
            SqlServerState.LoginFailed => ProbeFailure.LoginFailed,
            SqlServerState.Unreachable => ProbeFailure.Unreachable,
            SqlServerState.CertificateRejected => ProbeFailure.Certificate,
            _ => ProbeFailure.Other,
        };

    /// <summary>Reads what a probe's answer holds; a column that is missing or malformed reads as empty.</summary>
    /// <param name="row">The answer's attributes.</param>
    /// <returns>The result.</returns>
    internal static ProbeResult Result(IReadOnlyDictionary<string, string> row)
    {
        ArgumentNullException.ThrowIfNull(row);

        string? Text(string column) =>
            row.TryGetValue(column, out var value) && value.Length > 0 ? value : null;

        return new ProbeResult(
            Text("version"),
            Text("update_level"),
            Text("edition"),
            Text("platform"),
            int.TryParse(Text("databases"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                ? count
                : 0,
            List(Text("database_names"), "name").Take(20).ToList(),
            List(Text("missing"), "permission"));
    }

    // A list the probe's SQL writes with FOR JSON PATH, such as [{"name":"master"}].
    private static List<string> List(string? json, string property)
    {
        if (json is null)
        {
            return [];
        }

        try
        {
            return (JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json, _json) ?? [])
                .Select(item => item.GetValueOrDefault(property))
                .OfType<string>()
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? Offline(Agent agent, DateTimeOffset now) =>
        agent.Report.LastSeenAt switch
        {
            null => $"The agent {agent.Name} has never connected to Veyra.",
            { } seen when now - seen > SqlServerStatuses.OfflineAfter =>
                $"The agent {agent.Name} has not connected to Veyra for over 2 minutes.",
            _ => null,
        };

    // The connection to test and its password: the input's, or a saved server's, or the input's with the saved
    // server's password when none is given.
    private static (SqlServerConnection Connection, string Password) Connection(
        ProbeInput input,
        SqlServerTarget? saved,
        AgentReport agent)
    {
        if (saved is not null && string.IsNullOrWhiteSpace(input.Address))
        {
            return (saved.Server.Connection, saved.Password);
        }

        var connection = SqlServerConnectionRules.Check(
            input.Platform,
            input.Address,
            input.Port,
            input.Instance,
            input.Authentication,
            input.Login,
            input.Password,
            input.Encryption,
            input.Database,
            agent);

        if (!SqlServerConnectionRules.NeedsPassword(connection.Authentication))
        {
            return (connection, "");
        }

        var password = string.IsNullOrEmpty(input.Password) ? saved?.Password : input.Password;

        return string.IsNullOrEmpty(password)
            ? throw new AgentsValidationException("Enter the login's password.")
            : (connection, password);
    }

    // The connection as sent, for a probe no agent can run: it is shown back, never used.
    private static SqlServerConnection Unchecked(ProbeInput input, SqlServerTarget? saved) =>
        saved is not null && string.IsNullOrWhiteSpace(input.Address)
            ? saved.Server.Connection
            : new SqlServerConnection(
                input.Platform,
                input.Address?.Trim() ?? "",
                input.Port ?? SqlServerConnectionRules.DefaultPort,
                string.IsNullOrWhiteSpace(input.Instance) ? null : input.Instance.Trim(),
                input.Authentication,
                input.Login?.Trim() ?? "",
                input.Encryption,
                input.Database?.Trim());

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Probe {ProbeId} started on agent {AgentId} for server {ServerId}")]
    private partial void LogStarted(Guid probeId, Guid agentId, Guid? serverId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Probe {ProbeId} succeeded")]
    private partial void LogSucceeded(Guid probeId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Probe {ProbeId} failed: {Error}")]
    private partial void LogFailed(Guid probeId, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Probe {ProbeId} timed out")]
    private partial void LogTimedOut(Guid probeId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "An answer to probe {ProbeId} came with another key than its agent's; it was ignored")]
    private partial void LogStranger(Guid probeId);
}
