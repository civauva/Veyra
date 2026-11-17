using System.Security.Claims;
using System.Text.RegularExpressions;
using Veyra.Core.Settings;

namespace Veyra.Agents.Agents;

/// <summary>
/// Changes what an agent reads of the machine it runs on (VEY-35, ADR 0018). Nothing needs sending to the agent: the
/// next poll answers with its whole configuration, built from this setting, and the supervisor applies it when its
/// hash differs from what it runs.
/// </summary>
/// <param name="agents">Where agents are stored.</param>
/// <param name="history">The settings history, which names who changed what an agent reads.</param>
internal sealed partial class AgentHostService(AgentStore agents, IMonitoringHistory history)
{
    /// <summary>Sets what an agent reads of its host.</summary>
    /// <param name="agentId">The agent.</param>
    /// <param name="requested">
    /// The setting. Turning the host off also turns the Event Log, IIS, and Docker off, whatever was asked for them.
    /// </param>
    /// <param name="by">Who changes it, for the settings history.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The agent with its new setting, or <see langword="null"/> when there is no such agent.</returns>
    /// <exception cref="AgentsValidationException">
    /// The agent is the built-in one or is not installed, the Event Log or IIS is asked for on a host that does not
    /// run Windows, Docker is asked for on Windows, or the environment is not a plain name.
    /// </exception>
    public async Task<Agent?> SetWatchAsync(
        Guid agentId,
        HostWatch requested,
        ClaimsPrincipal by,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requested);

        if (await agents.FindAsync(agentId, cancellationToken) is not { } agent)
        {
            return null;
        }

        var environment = requested.Environment?.Trim() ?? "";

        Require(
            agent.Placement != AgentPlacement.BuiltIn,
            "The built-in agent runs in a container; install an agent on the host to watch it.");
        Require(agent.IngestionKeyId is not null, "Install the agent on its machine before it can watch it.");
        Require(
            EnvironmentPattern().IsMatch(environment),
            "Name the environment with letters, digits, dots, dashes, or underscores, such as production.");

        var watch = requested.Host
            ? requested with { Environment = environment }
            : new HostWatch(Host: false, EventLog: false, Iis: false, environment, Docker: false);

        Require(
            agent.Report.IsWindows || (!watch.EventLog && !watch.Iis),
            "The Event Log and IIS are read on Windows only.");

        // The Docker template reads the Linux socket and log folder; Docker Desktop on Windows has neither.
        Require(!agent.Report.IsWindows || !watch.Docker, "Docker containers are read on Linux only.");

        await agents.SaveWatchAsync(agentId, watch, cancellationToken);

        if (ChangedFields(agent.Watch, watch) is { Count: > 0 } fields)
        {
            await history.RecordAsync(
                new MonitoringChange(MonitoringSubject.Agent, agent.Name, MonitoringChangeKind.Changed, fields),
                by,
                cancellationToken);
        }

        return agent with { Watch = watch };
    }

    // What a change turned on or off, in the words the Agents page uses.
    private static List<string> ChangedFields(HostWatch before, HostWatch after) =>
        new (bool Changed, string Field)[]
        {
            (before.Host != after.Host, "host"),
            (before.EventLog != after.EventLog, "Event Log"),
            (before.Iis != after.Iis, "IIS"),
            (before.Docker != after.Docker, "Docker"),
            (before.Environment != after.Environment, "environment"),
        }.Where(entry => entry.Changed).Select(entry => entry.Field).ToList();

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new AgentsValidationException(message);
        }
    }

    // The environment becomes an attribute of the host's telemetry, so it stays a plain token, as for a SQL Server.
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex EnvironmentPattern();
}
