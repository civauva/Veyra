using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Veyra.Core.Security;

namespace Veyra.Agents.Agents;

/// <summary>
/// Sets up the built-in agent at startup: the agent beside Veyra in its Docker Compose deployment (ADR 0019).
/// </summary>
/// <remarks>
/// <para>
/// Veyra and the agent's container share a folder. Veyra writes the OpAMP supervisor's start-up file there, with the
/// agent's own key; the agent's container waits for it, then connects and gets everything else over OpAMP. Nobody
/// copies a key or edits a file.
/// </para>
/// <para>
/// Only the key's hash is stored, so the file is the one place the key exists. When the file is lost or its key is
/// revoked, the same agent gets a new key and a new file, and the old key is revoked.
/// </para>
/// </remarks>
/// <param name="agents">Where agents are stored.</param>
/// <param name="issuer">Issues and revokes the agent's key.</param>
/// <param name="validator">Checks the key found in the file.</param>
/// <param name="options">Where the shared folder is, and how the agent reaches Veyra.</param>
/// <param name="clock">The clock for the agent's creation time.</param>
/// <param name="logger">Records what was set up.</param>
internal sealed partial class BuiltInAgentSetup(
    AgentStore agents,
    IIngestionKeyIssuer issuer,
    IIngestionKeyValidator validator,
    IOptionsMonitor<AgentsOptions> options,
    TimeProvider clock,
    ILogger<BuiltInAgentSetup> logger)
{
    /// <summary>The start-up file's name in the shared folder.</summary>
    public const string FileName = "supervisor.yaml";

    /// <summary>What the Agents page calls the built-in agent.</summary>
    public const string AgentName = "Built in";

    /// <summary>Makes sure the built-in agent exists and its start-up file holds a working key.</summary>
    /// <param name="cancellationToken">Cancels the setup.</param>
    /// <returns>A task that completes when the agent is ready to connect.</returns>
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;

        if (string.IsNullOrWhiteSpace(settings.BuiltInDirectory))
        {
            return;
        }

        var path = Path.Combine(settings.BuiltInDirectory, FileName);
        var agent = await agents.FindBuiltInAsync(cancellationToken);

        if (agent is not null && await FileHoldsKeyOfAsync(path, agent, cancellationToken))
        {
            return;
        }

        var key = await issuer.IssueAsync($"Agent: {AgentName}", cancellationToken);

        if (agent is null)
        {
            agent = new Agent(
                Id: Guid.CreateVersion7(),
                Name: AgentName,
                Placement: AgentPlacement.BuiltIn,
                IngestionKeyId: key.KeyId,
                CreatedAt: clock.GetUtcNow(),
                Report: AgentReport.None,
                Watch: HostWatch.None);

            await agents.InsertAsync(agent, cancellationToken);
            LogCreated(agent.Id);
        }
        else
        {
            await agents.ReplaceKeyAsync(agent.Id, key.KeyId, cancellationToken);

            if (agent.IngestionKeyId is { } old)
            {
                await issuer.RevokeAsync(old, cancellationToken);
            }

            LogNewKey(agent.Id);
        }

        var serverUrl = new Uri(new Uri(settings.BuiltInServerUrl), "/v1/opamp");
        var startup = SupervisorConfig.Write(serverUrl, key.Secret, AgentPlatform.Container);
        await WriteAsync(path, startup, cancellationToken);
    }

    /// <summary>Reads the key from a start-up file's text.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The key, or <see langword="null"/> when the text holds none.</returns>
    internal static string? ReadKey(string text) =>
        KeyLine().Match(text) is { Success: true } match ? match.Groups["key"].Value : null;

    private async Task<bool> FileHoldsKeyOfAsync(string path, Agent agent, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var key = ReadKey(await File.ReadAllTextAsync(path, cancellationToken));

        if (key is null)
        {
            return false;
        }

        var check = await validator.ValidateAsync(key, cancellationToken);

        return check.IsValid && check.KeyId == agent.IngestionKeyId;
    }

    // Written beside its final place and moved over it, so the agent never reads half a file. Only Veyra's user may
    // read it; the agent's container runs as that user.
    private static async Task WriteAsync(string path, string text, CancellationToken cancellationToken)
    {
        var temporary = path + ".new";

        await File.WriteAllTextAsync(temporary, text, cancellationToken);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temporary, path, overwrite: true);
    }

    [GeneratedRegex(@"Authorization:\s*""?Bearer (?<key>[A-Za-z0-9_-]+)""?")]
    private static partial Regex KeyLine();

    [LoggerMessage(Level = LogLevel.Information, Message = "Built-in agent {AgentId} set up")]
    private partial void LogCreated(Guid agentId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Security: built-in agent {AgentId} given a new key; its start-up file was lost or its key revoked")]
    private partial void LogNewKey(Guid agentId);
}
