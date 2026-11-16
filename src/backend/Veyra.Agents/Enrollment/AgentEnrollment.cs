using Microsoft.Extensions.Logging;
using Veyra.Agents.Agents;
using Veyra.Core.Security;

namespace Veyra.Agents.Enrollment;

/// <summary>
/// Adds agents for other networks and installs them with one-time codes (ADR 0019, VEY-40). An Admin adds an agent on
/// the Agents page and gets a code; the installer, run inside that network, exchanges the code for the agent's own key
/// and start-up file.
/// </summary>
/// <param name="agents">Where agents are stored.</param>
/// <param name="codes">Where codes are stored, by their hashes.</param>
/// <param name="issuer">Issues and revokes agent keys.</param>
/// <param name="clock">The clock for creation and expiry.</param>
/// <param name="logger">Records the security events: agents added, installed, and removed.</param>
internal sealed partial class AgentEnrollment(
    AgentStore agents,
    EnrollmentStore codes,
    IIngestionKeyIssuer issuer,
    TimeProvider clock,
    ILogger<AgentEnrollment> logger)
{
    /// <summary>How long a code works.</summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(60);

    private const int _maxNameLength = 60;

    /// <summary>Adds an agent for another network, not installed yet, with its first code.</summary>
    /// <param name="name">What the Agents page calls it, such as "Company network".</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The agent and its code, shown once.</returns>
    /// <exception cref="AgentsValidationException">The name is empty or too long.</exception>
    public async Task<AddedAgent> AddAsync(string name, CancellationToken cancellationToken)
    {
        var trimmed = name?.Trim() ?? "";

        if (trimmed.Length is 0 or > _maxNameLength || trimmed.Any(char.IsControl))
        {
            throw new AgentsValidationException($"Name the agent in 1 to {_maxNameLength} characters.");
        }

        var agent = new Agent(
            Id: Guid.CreateVersion7(),
            Name: trimmed,
            Placement: AgentPlacement.Remote,
            IngestionKeyId: null,
            CreatedAt: clock.GetUtcNow(),
            Report: AgentReport.None);

        await agents.InsertAsync(agent, cancellationToken);
        var code = await IssueAsync(agent.Id, cancellationToken);
        LogAdded(agent.Id, agent.Name);

        return new AddedAgent(agent, code.Code, code.ExpiresAt);
    }

    /// <summary>Gives an agent for another network a new code, to install it for the first time or again.</summary>
    /// <param name="agentId">The agent.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The code, or <see langword="null"/> when there is no such agent in another network.</returns>
    public async Task<IssuedCode?> NewCodeAsync(Guid agentId, CancellationToken cancellationToken)
    {
        if (await agents.FindAsync(agentId, cancellationToken) is not { Placement: AgentPlacement.Remote })
        {
            return null;
        }

        return await IssueAsync(agentId, cancellationToken);
    }

    /// <summary>
    /// Installs an agent: uses its code, issues its own key, and writes its start-up file. A key it had before, from an
    /// earlier install, is revoked.
    /// </summary>
    /// <param name="code">The code as typed or pasted.</param>
    /// <param name="platform">Where the installer puts the agent's programs.</param>
    /// <param name="veyraUrl">Veyra's address as the installer reached it, which the agent will use too.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The agent's name and start-up file, or <see langword="null"/> for a wrong, used, or expired code.
    /// </returns>
    public async Task<EnrolledAgent?> EnrollAsync(
        string code,
        AgentPlatform platform,
        Uri veyraUrl,
        CancellationToken cancellationToken)
    {
        if (EnrollmentCode.Normalize(code) is not { } normalized)
        {
            return null;
        }

        if (await codes.UseAsync(EnrollmentCode.Hash(normalized), clock.GetUtcNow(), cancellationToken) is not { } id
            || await agents.FindAsync(id, cancellationToken) is not { } agent)
        {
            return null;
        }

        var key = await issuer.IssueAsync($"Agent: {agent.Name}", cancellationToken);
        await agents.ReplaceKeyAsync(agent.Id, key.KeyId, cancellationToken);

        if (agent.IngestionKeyId is { } old)
        {
            await issuer.RevokeAsync(old, cancellationToken);
        }

        LogEnrolled(agent.Id, agent.Name, platform);

        var opamp = new Uri(veyraUrl, "/v1/opamp");

        return new EnrolledAgent(agent.Name, SupervisorConfig.Write(opamp, key.Secret, platform));
    }

    /// <summary>
    /// Removes an agent for another network: revokes its key, so it can no longer connect or send, and its codes.
    /// </summary>
    /// <param name="agentId">The agent.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Whether there was such an agent.</returns>
    /// <exception cref="AgentsValidationException">
    /// The agent is the built-in one, which comes and goes with Veyra.
    /// </exception>
    /// <exception cref="AgentsConflictException">The agent still reads servers.</exception>
    public async Task<bool> RemoveAsync(Guid agentId, CancellationToken cancellationToken)
    {
        if (await agents.FindAsync(agentId, cancellationToken) is not { } agent)
        {
            return false;
        }

        if (agent.Placement == AgentPlacement.BuiltIn)
        {
            throw new AgentsValidationException("The built-in agent comes with Veyra and cannot be removed.");
        }

        if (await agents.CountServersAsync(agentId, cancellationToken) is var servers and > 0)
        {
            throw new AgentsConflictException(
                servers == 1
                    ? $"{agent.Name} still reads a SQL Server. Give it to another agent, or remove it, first."
                    : $"{agent.Name} still reads {servers} SQL Servers. Give them to another agent, or remove them, "
                        + "first.");
        }

        await codes.DeleteForAgentAsync(agentId, cancellationToken);
        await agents.RevokeAsync(agentId, clock.GetUtcNow(), cancellationToken);

        if (agent.IngestionKeyId is { } key)
        {
            await issuer.RevokeAsync(key, cancellationToken);
        }

        LogRemoved(agent.Id, agent.Name);

        return true;
    }

    private async Task<IssuedCode> IssueAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var code = EnrollmentCode.Generate();
        var now = clock.GetUtcNow();
        var expiresAt = now + CodeLifetime;

        await codes.InsertAsync(agentId, EnrollmentCode.Hash(code), now, expiresAt, cancellationToken);

        return new IssuedCode(code, expiresAt);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Security: agent {AgentId} {AgentName} added; waiting to be installed")]
    private partial void LogAdded(Guid agentId, string agentName);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Security: agent {AgentId} {AgentName} installed on {Platform} with a one-time code")]
    private partial void LogEnrolled(Guid agentId, string agentName, AgentPlatform platform);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Security: agent {AgentId} {AgentName} removed; its key is revoked")]
    private partial void LogRemoved(Guid agentId, string agentName);
}
