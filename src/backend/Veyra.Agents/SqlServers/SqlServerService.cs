using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Veyra.Agents.Agents;

namespace Veyra.Agents.SqlServers;

/// <summary>
/// Adds, changes, and removes the SQL Servers agents read, as the Settings screen asks. A change reaches the agent
/// with its next poll, because its configuration is built from what is stored here.
/// </summary>
/// <param name="servers">Where servers are stored.</param>
/// <param name="agents">Checks the agent a server is given to.</param>
/// <param name="passwords">Encrypts passwords before they are stored.</param>
/// <param name="clock">The clock for creation and change times.</param>
/// <param name="logger">Records who changed what, without passwords.</param>
internal sealed partial class SqlServerService(
    SqlServerStore servers,
    AgentStore agents,
    SqlServerPasswordProtector passwords,
    TimeProvider clock,
    ILogger<SqlServerService> logger)
{
    /// <summary>Lists every server, by name, without passwords.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The servers.</returns>
    public async Task<IReadOnlyList<SqlServer>> ListAsync(CancellationToken cancellationToken) =>
        (await servers.ListAsync(cancellationToken)).Select(entry => entry.Server).ToList();

    /// <summary>Adds a server.</summary>
    /// <param name="input">The server, with its password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stored server.</returns>
    /// <exception cref="AgentsValidationException">The input breaks a rule.</exception>
    /// <exception cref="AgentsConflictException">Another server has the name.</exception>
    public async Task<SqlServer> AddAsync(SqlServerInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrEmpty(input.Password))
        {
            throw new AgentsValidationException("Enter the login's password.");
        }

        var now = clock.GetUtcNow();
        var server = await ToServerAsync(Guid.CreateVersion7(), input, now, now, cancellationToken);

        await servers.InsertAsync(server, passwords.Protect(input.Password), cancellationToken);
        LogAdded(server.Id, server.Name, server.AgentId);

        return server;
    }

    /// <summary>Changes a server. A missing password keeps the stored one.</summary>
    /// <param name="id">The server's ID.</param>
    /// <param name="input">The server as it should be.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stored server, or <see langword="null"/> when no server has the ID.</returns>
    /// <exception cref="AgentsValidationException">The input breaks a rule.</exception>
    /// <exception cref="AgentsConflictException">Another server has the name.</exception>
    public async Task<SqlServer?> UpdateAsync(Guid id, SqlServerInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (await servers.FindAsync(id, cancellationToken) is not { } stored)
        {
            return null;
        }

        var server = await ToServerAsync(id, input, stored.Server.CreatedAt, clock.GetUtcNow(), cancellationToken);
        var password = string.IsNullOrEmpty(input.Password) ? null : passwords.Protect(input.Password);

        if (!await servers.UpdateAsync(server, password, cancellationToken))
        {
            return null;
        }

        LogChanged(server.Id, server.Name, password is not null);

        return server;
    }

    /// <summary>Removes a server. Its agent stops reading it with its next poll.</summary>
    /// <param name="id">The server's ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Whether a server had the ID.</returns>
    public async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        var removed = await servers.DeleteAsync(id, cancellationToken);

        if (removed)
        {
            LogRemoved(id);
        }

        return removed;
    }

    /// <summary>The servers one agent reads, with their passwords decrypted, for its configuration.</summary>
    /// <param name="agentId">The agent's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>
    /// The servers. A server whose password can no longer be decrypted, because the key ring was replaced, is left out
    /// and logged: it needs its password entered again.
    /// </returns>
    public async Task<IReadOnlyList<SqlServerTarget>> TargetsAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var targets = new List<SqlServerTarget>();

        foreach (var (server, protectedPassword) in await servers.ListForAgentAsync(agentId, cancellationToken))
        {
            if (passwords.Unprotect(protectedPassword) is { } password)
            {
                targets.Add(new SqlServerTarget(server, password));
            }
            else
            {
                LogUnreadablePassword(server.Id, server.Name);
            }
        }

        return targets;
    }

    private async Task<SqlServer> ToServerAsync(
        Guid id,
        SqlServerInput input,
        DateTimeOffset createdAt,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var name = input.Name?.Trim() ?? "";
        var address = input.Address?.Trim() ?? "";
        var environment = input.Environment?.Trim() ?? "";
        var login = input.Login?.Trim() ?? "";

        Require(NamePattern().IsMatch(name), "Name the server with letters, digits, dots, dashes, or underscores.");
        Require(
            address.Length <= 253 && AddressPattern().IsMatch(address),
            "Enter the server's host name or IP address, such as sql-prod-01.corp.local or 10.0.0.5.");
        Require(input.Port is >= 1 and <= 65535, "The port must be from 1 to 65535.");
        Require(
            NamePattern().IsMatch(environment),
            "Name the environment with letters, digits, dots, dashes, or underscores, such as production.");
        Require(login.Length is > 0 and <= 128, "Enter the SQL login, at most 128 characters.");
        Require(input.Password is null || input.Password.Length <= 128, "The password may have at most 128 letters.");
        Require(Enum.IsDefined(input.Encryption), "Choose how the connection is encrypted.");

        var agent = input.AgentId is { } agentId
            ? await agents.FindAsync(agentId, cancellationToken)
            : await agents.FindBuiltInAsync(cancellationToken);

        Require(agent is not null, input.AgentId is null
            ? "Veyra has no built-in agent. Choose the agent that reads this server."
            : "The chosen agent no longer exists. Choose another.");

        return new SqlServer(
            Id: id,
            Name: name,
            Address: address,
            Port: input.Port,
            Environment: environment,
            AgentId: agent!.Id,
            Login: login,
            Encryption: input.Encryption,
            CreatedAt: createdAt,
            ChangedAt: changedAt);
    }

    private static void Require(bool holds, string message)
    {
        if (!holds)
        {
            throw new AgentsValidationException(message);
        }
    }

    // The address goes into the agent's connection URL, so it holds a host name, an IPv4 address, or an IPv6 address
    // in brackets, and nothing that would end the host part: no '?', '/', '@', or ':' outside the brackets.
    [GeneratedRegex(@"^(?:[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?|\[[0-9A-Fa-f:.]+\])$")]
    private static partial Regex AddressPattern();

    // A server's name becomes its block and server.address in its telemetry, so it stays a plain token.
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,62}$")]
    private static partial Regex NamePattern();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "SQL Server {ServerId} {ServerName} added to agent {AgentId}")]
    private partial void LogAdded(Guid serverId, string serverName, Guid agentId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "SQL Server {ServerId} {ServerName} changed; new password: {PasswordChanged}")]
    private partial void LogChanged(Guid serverId, string serverName, bool passwordChanged);

    [LoggerMessage(Level = LogLevel.Information, Message = "SQL Server {ServerId} removed")]
    private partial void LogRemoved(Guid serverId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The password of SQL Server {ServerId} {ServerName} cannot be decrypted; enter it again")]
    private partial void LogUnreadablePassword(Guid serverId, string serverName);
}
