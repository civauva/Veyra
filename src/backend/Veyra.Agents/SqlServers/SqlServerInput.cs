namespace Veyra.Agents.SqlServers;

/// <summary>A SQL Server as the Settings screen or the setup wizard sends it, to add one or to change one.</summary>
/// <param name="Name">
/// Its name in Veyra, such as <c>sql-prod-01</c>: letters, digits, dots, dashes, and underscores, at most 63.
/// </param>
/// <param name="Address">
/// The host name or IP address the agent connects to, or <c>HOST\INSTANCE</c>, which is split into the address and
/// <paramref name="Instance"/>.
/// </param>
/// <param name="Port">The TCP port, from 1 to 65535; 1433 when left out. Not used with a named instance.</param>
/// <param name="Environment">The environment its telemetry is filed under, such as <c>production</c>.</param>
/// <param name="AgentId">The agent that reads it; <see langword="null"/> for the built-in agent.</param>
/// <param name="Login">
/// The login the agent signs in with: a SQL login, or <c>DOMAIN\name</c> for Windows (NTLM); none for integrated
/// authentication.
/// </param>
/// <param name="Password">
/// The login's password. Required to add a server that signs in with one; <see langword="null"/> when changing one
/// keeps the stored password.
/// </param>
/// <param name="Encryption">How the connection is encrypted.</param>
/// <param name="Platform">The kind of SQL Server it is; a SQL Server when left out.</param>
/// <param name="Database">
/// The database to read, required for an Azure SQL Database and left out for the other kinds.
/// </param>
/// <param name="Instance">
/// The named instance (ADR 0028), reached through the SQL Server Browser on UDP 1434; left out for a port.
/// </param>
/// <param name="Authentication">How the agent signs in; SQL authentication when left out.</param>
/// <param name="Modules">
/// Modules to switch or time; those left out keep what the server has, their defaults for a new one.
/// <see langword="null"/> changes none.
/// </param>
public sealed record SqlServerInput(
    string Name,
    string Address,
    int? Port,
    string Environment,
    Guid? AgentId,
    string? Login,
    string? Password,
    SqlServerEncryption Encryption,
    SqlServerPlatform Platform = SqlServerPlatform.SqlServer,
    string? Database = null,
    string? Instance = null,
    SqlServerAuthentication Authentication = SqlServerAuthentication.Sql,
    IReadOnlyList<SqlServerModuleChoice>? Modules = null)
{
    /// <summary>Keeps the password out of logs and debugger views, which print records by their members.</summary>
    /// <returns>The server's name and address.</returns>
    public override string ToString() => $"SqlServerInput {{ Name = {Name}, Address = {Address}:{Port} }}";
}
