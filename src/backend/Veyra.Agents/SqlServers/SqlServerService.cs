using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Veyra.Agents.Agents;
using Veyra.Agents.Integrations;
using Veyra.Core.Settings;

namespace Veyra.Agents.SqlServers;

/// <summary>
/// Adds, changes, and removes the SQL Servers agents read, as the Settings screen and the setup wizard ask. A change
/// reaches the agent with its next poll, because its configuration is built from what is stored here.
/// </summary>
/// <param name="servers">Where servers are stored.</param>
/// <param name="agents">Checks the agent a server is given to.</param>
/// <param name="passwords">Encrypts passwords before they are stored.</param>
/// <param name="clock">The clock for creation and change times.</param>
/// <param name="history">The settings history, which names who changed which fields, never their values.</param>
/// <param name="logger">Records changes in Veyra's log, without passwords.</param>
internal sealed partial class SqlServerService(
    SqlServerStore servers,
    AgentStore agents,
    SqlServerPasswordProtector passwords,
    TimeProvider clock,
    IMonitoringHistory history,
    ILogger<SqlServerService> logger)
{
    /// <summary>The field a template version change touches, in the history.</summary>
    public const string VersionField = "version";

    /// <summary>Lists every server, by name, without passwords.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The servers.</returns>
    public async Task<IReadOnlyList<SqlServer>> ListAsync(CancellationToken cancellationToken) =>
        (await servers.ListAsync(cancellationToken)).Select(entry => entry.Server).ToList();

    /// <summary>Finds a server, without its password.</summary>
    /// <param name="id">The server's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The server, or <see langword="null"/> when no server has the ID.</returns>
    public async Task<SqlServer?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        (await servers.FindAsync(id, cancellationToken))?.Server;

    /// <summary>Finds a server with its decrypted password, for a probe of its stored connection.</summary>
    /// <param name="id">The server's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>
    /// The server and its password; <see langword="null"/> when no server has the ID or its password can no longer
    /// be decrypted.
    /// </returns>
    public async Task<SqlServerTarget?> FindTargetAsync(Guid id, CancellationToken cancellationToken) =>
        await servers.FindAsync(id, cancellationToken) is { } stored
        && passwords.Unprotect(stored.ProtectedPassword) is { } password
            ? new SqlServerTarget(stored.Server, password)
            : null;

    /// <summary>Adds a server. It takes the current template version.</summary>
    /// <param name="input">The server, with its password.</param>
    /// <param name="by">Who adds it, for the settings history.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stored server.</returns>
    /// <exception cref="AgentsValidationException">The input breaks a rule.</exception>
    /// <exception cref="AgentsConflictException">Another server has the name.</exception>
    public async Task<SqlServer> AddAsync(SqlServerInput input, ClaimsPrincipal by, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(by);

        if (SqlServerConnectionRules.NeedsPassword(input.Authentication) && string.IsNullOrEmpty(input.Password))
        {
            throw new AgentsValidationException("Enter the login's password.");
        }

        var now = clock.GetUtcNow();
        var server = await ToServerAsync(
            Guid.CreateVersion7(),
            input,
            now,
            now,
            SqlServerModules.Defaults(),
            cancellationToken) with
        {
            TemplateVersion = SqlServerTemplates.Current.Version,
            TemplateSetAt = now,
            TemplateSetBy = by.Identity?.Name,
        };

        await servers.InsertAsync(server, passwords.Protect(input.Password ?? ""), cancellationToken);
        LogAdded(server.Id, server.Name, server.AgentId);
        await RecordAsync(
            new MonitoringChange(MonitoringSubject.SqlServer, server.Name, MonitoringChangeKind.Added, []),
            by,
            cancellationToken);

        return server;
    }

    /// <summary>Changes a server. A missing password keeps the stored one.</summary>
    /// <param name="id">The server's ID.</param>
    /// <param name="input">The server as it should be.</param>
    /// <param name="by">Who changes it, for the settings history.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stored server, or <see langword="null"/> when no server has the ID.</returns>
    /// <exception cref="AgentsValidationException">The input breaks a rule.</exception>
    /// <exception cref="AgentsConflictException">Another server has the name.</exception>
    public async Task<SqlServer?> UpdateAsync(
        Guid id,
        SqlServerInput input,
        ClaimsPrincipal by,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (await servers.FindAsync(id, cancellationToken) is not { } stored)
        {
            return null;
        }

        // A server that signed in as the agent's service account has no password to keep.
        if (SqlServerConnectionRules.NeedsPassword(input.Authentication)
            && !SqlServerConnectionRules.NeedsPassword(stored.Server.Authentication)
            && string.IsNullOrEmpty(input.Password))
        {
            throw new AgentsValidationException("Enter the login's password.");
        }

        var server = await ToServerAsync(
            id,
            input,
            stored.Server.CreatedAt,
            clock.GetUtcNow(),
            stored.Server.Choices,
            cancellationToken) with
        {
            TemplateVersion = stored.Server.TemplateVersion,
            TemplateSetAt = stored.Server.TemplateSetAt,
            TemplateSetBy = stored.Server.TemplateSetBy,
        };

        var password = input.Authentication == SqlServerAuthentication.Integrated
            ? passwords.Protect("")
            : string.IsNullOrEmpty(input.Password) ? null : passwords.Protect(input.Password);

        if (!await servers.UpdateAsync(server, password, cancellationToken))
        {
            return null;
        }

        LogChanged(server.Id, server.Name, password is not null);

        if (ChangedFields(stored.Server, server, !string.IsNullOrEmpty(input.Password)) is { Count: > 0 } fields)
        {
            await RecordAsync(
                new MonitoringChange(MonitoringSubject.SqlServer, server.Name, MonitoringChangeKind.Changed, fields),
                by,
                cancellationToken);
        }

        return server;
    }

    /// <summary>
    /// Moves a server to a shipped template version: Update to the current one, Roll back to the previous one
    /// (ADR 0028). Its agent reads with it from its next poll.
    /// </summary>
    /// <param name="id">The server's ID.</param>
    /// <param name="version">The version.</param>
    /// <param name="by">Who moves it, for the history.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stored server, or <see langword="null"/> when no server has the ID.</returns>
    /// <exception cref="AgentsValidationException">Veyra does not ship the version.</exception>
    public async Task<SqlServer?> SetVersionAsync(
        Guid id,
        string version,
        ClaimsPrincipal by,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(by);

        if (SqlServerTemplates.Find(version?.Trim()) is not { } template)
        {
            throw new AgentsValidationException(
                $"Veyra ships versions {SqlServerTemplates.Current.Version} and {SqlServerTemplates.Previous.Version}"
                + " of the SQL Server template.");
        }

        if (await servers.FindAsync(id, cancellationToken) is not { } stored)
        {
            return null;
        }

        if (stored.Server.Version == template.Version)
        {
            return stored.Server;
        }

        var now = clock.GetUtcNow();
        var server = stored.Server with
        {
            TemplateVersion = template.Version,
            TemplateSetAt = now,
            TemplateSetBy = by.Identity?.Name,
            ChangedAt = now,
        };

        if (!await servers.UpdateAsync(server, null, cancellationToken))
        {
            return null;
        }

        LogVersion(server.Id, server.Name, stored.Server.Version, template.Version);
        await RecordAsync(
            new MonitoringChange(
                MonitoringSubject.SqlServer,
                server.Name,
                MonitoringChangeKind.Changed,
                [VersionField],
                stored.Server.Version,
                template.Version),
            by,
            cancellationToken);

        return server;
    }

    /// <summary>Removes a server. Its agent stops reading it with its next poll.</summary>
    /// <param name="id">The server's ID.</param>
    /// <param name="by">Who removes it, for the settings history.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Whether a server had the ID.</returns>
    public async Task<bool> RemoveAsync(Guid id, ClaimsPrincipal by, CancellationToken cancellationToken)
    {
        if (await servers.FindAsync(id, cancellationToken) is not { } stored
            || !await servers.DeleteAsync(id, cancellationToken))
        {
            return false;
        }

        LogRemoved(id);
        await RecordAsync(
            new MonitoringChange(MonitoringSubject.SqlServer, stored.Server.Name, MonitoringChangeKind.Removed, []),
            by,
            cancellationToken);

        return true;
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

    private Task RecordAsync(MonitoringChange change, ClaimsPrincipal by, CancellationToken cancellationToken) =>
        history.RecordAsync(change, by, cancellationToken);

    // The fields a change touched, in the words the history shows. An entered password counts as changed: only its
    // encrypted form is stored, so it cannot be compared.
    private static List<string> ChangedFields(SqlServer before, SqlServer after, bool password) =>
        new (bool Changed, string Field)[]
        {
            (before.Name != after.Name, "name"),
            (before.Platform != after.Platform, "kind"),
            (before.Database != after.Database, "database"),
            (before.Address != after.Address, "address"),
            (before.Port != after.Port, "port"),
            (before.Instance != after.Instance, "instance"),
            (before.Environment != after.Environment, "environment"),
            (before.AgentId != after.AgentId, "agent"),
            (before.Authentication != after.Authentication, "authentication"),
            (before.Login != after.Login, "login"),
            (password, "password"),
            (before.Encryption != after.Encryption, "encryption"),
            (!before.Choices.SequenceEqual(after.Choices), "modules"),
        }.Where(entry => entry.Changed).Select(entry => entry.Field).ToList();

    private async Task<SqlServer> ToServerAsync(
        Guid id,
        SqlServerInput input,
        DateTimeOffset createdAt,
        DateTimeOffset changedAt,
        IReadOnlyList<SqlServerModuleChoice> currentModules,
        CancellationToken cancellationToken)
    {
        var name = input.Name?.Trim() ?? "";
        var environment = input.Environment?.Trim() ?? "";

        Require(NamePattern().IsMatch(name), "Name the server with letters, digits, dots, dashes, or underscores.");
        Require(
            NamePattern().IsMatch(environment),
            "Name the environment with letters, digits, dots, dashes, or underscores, such as production.");

        var agent = input.AgentId is { } agentId
            ? await agents.FindAsync(agentId, cancellationToken)
            : await agents.FindBuiltInAsync(cancellationToken);

        Require(agent is not null, input.AgentId is null
            ? "Veyra has no built-in agent. Choose the agent that reads this server."
            : "The chosen agent no longer exists. Choose another.");

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
            agent!.Report);
        var modules = SqlServerModules.Check(input.Modules, currentModules);

        return new SqlServer(
            Id: id,
            Name: name,
            Address: connection.Address,
            Port: connection.Port,
            Environment: environment,
            AgentId: agent.Id,
            Login: connection.Login,
            Encryption: connection.Encryption,
            CreatedAt: createdAt,
            ChangedAt: changedAt,
            Platform: connection.Platform,
            Database: connection.Database,
            Instance: connection.Instance,
            Authentication: connection.Authentication,
            Modules: modules);
    }

    private static void Require(bool holds, string message)
    {
        if (!holds)
        {
            throw new AgentsValidationException(message);
        }
    }

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

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "SQL Server {ServerId} {ServerName} moved from template {FromVersion} to {ToVersion}")]
    private partial void LogVersion(Guid serverId, string serverName, string fromVersion, string toVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "SQL Server {ServerId} removed")]
    private partial void LogRemoved(Guid serverId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The password of SQL Server {ServerId} {ServerName} cannot be decrypted; enter it again")]
    private partial void LogUnreadablePassword(Guid serverId, string serverName);
}
