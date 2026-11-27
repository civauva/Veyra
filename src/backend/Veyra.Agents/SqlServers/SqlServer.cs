using Veyra.Agents.Integrations;

namespace Veyra.Agents.SqlServers;

/// <summary>
/// A SQL Server that a Veyra agent reads, as set up on the Settings screen. Its password is kept apart, encrypted.
/// </summary>
/// <param name="Id">The server's ID. Its agent's Collector components are named after it.</param>
/// <param name="Name">
/// The server's name in Veyra, its database block (ADR 0018), such as <c>sql-prod-01</c>. Unique.
/// </param>
/// <param name="Address">The host name or IP address the agent connects to.</param>
/// <param name="Port">The TCP port, 1433 unless the server listens elsewhere. Not used with a named instance.</param>
/// <param name="Environment">The environment its telemetry is filed under, such as <c>production</c>.</param>
/// <param name="AgentId">The agent that reads it.</param>
/// <param name="Login">The login the agent signs in with; empty for integrated Windows authentication.</param>
/// <param name="Encryption">How the connection is encrypted.</param>
/// <param name="CreatedAt">When it was added.</param>
/// <param name="ChangedAt">When it was last changed.</param>
/// <param name="Platform">The kind of SQL Server it is.</param>
/// <param name="Database">
/// The one database it is read in, for an Azure SQL Database; otherwise <see langword="null"/>.
/// </param>
/// <param name="Instance">Its named instance (ADR 0028), or <see langword="null"/> when it is reached by port.</param>
/// <param name="Authentication">How the agent signs in.</param>
/// <param name="Modules">
/// Each module's switch and interval, in the catalog's order; <see langword="null"/> means every module at its
/// default.
/// </param>
/// <param name="TemplateVersion">
/// The version of the template it is read with; <see langword="null"/> means the current one.
/// </param>
/// <param name="TemplateSetAt">When that version was set.</param>
/// <param name="TemplateSetBy">Who set it; <see langword="null"/> for Veyra itself.</param>
internal sealed record SqlServer(
    Guid Id,
    string Name,
    string Address,
    int Port,
    string Environment,
    Guid AgentId,
    string Login,
    SqlServerEncryption Encryption,
    DateTimeOffset CreatedAt,
    DateTimeOffset ChangedAt,
    SqlServerPlatform Platform = SqlServerPlatform.SqlServer,
    string? Database = null,
    string? Instance = null,
    SqlServerAuthentication Authentication = SqlServerAuthentication.Sql,
    IReadOnlyList<SqlServerModuleChoice>? Modules = null,
    string? TemplateVersion = null,
    DateTimeOffset? TemplateSetAt = null,
    string? TemplateSetBy = null)
{
    /// <summary>Every module's switch and interval, the defaults filled in.</summary>
    public IReadOnlyList<SqlServerModuleChoice> Choices => Modules ?? SqlServerModules.Defaults();

    /// <summary>Where and how the agent connects.</summary>
    public SqlServerConnection Connection =>
        new(Platform, Address, Port, Instance, Authentication, Login, Encryption, Database);

    /// <summary>The version of the template it is read with.</summary>
    public string Version => TemplateVersion ?? SqlServerTemplates.Current.Version;
}
