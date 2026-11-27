using Veyra.Agents.SqlServers;

namespace Veyra.Agents.Probes;

/// <summary>
/// The connection to test, as the setup wizard sends it for Test connection or the integration page for Validate now
/// (ADR 0028).
/// </summary>
/// <param name="AgentId">
/// The agent to run it; <see langword="null"/> for the saved server's agent, or else the built-in agent.
/// </param>
/// <param name="ServerId">
/// A saved server to validate. Without an address, its stored connection and password are tested; with one, the
/// connection given, with the stored password when none is given.
/// </param>
/// <param name="Platform">The kind of SQL Server.</param>
/// <param name="Address">The host name or IP address, or <c>HOST\INSTANCE</c>.</param>
/// <param name="Port">The TCP port; 1433 when left out.</param>
/// <param name="Instance">The named instance, if any.</param>
/// <param name="Authentication">How the agent signs in.</param>
/// <param name="Login">The login; none for integrated authentication.</param>
/// <param name="Password">The password; forgotten when the probe finishes.</param>
/// <param name="Encryption">How the connection is encrypted.</param>
/// <param name="Database">The database of an Azure SQL Database.</param>
public sealed record ProbeInput(
    Guid? AgentId = null,
    Guid? ServerId = null,
    SqlServerPlatform Platform = SqlServerPlatform.SqlServer,
    string? Address = null,
    int? Port = null,
    string? Instance = null,
    SqlServerAuthentication Authentication = SqlServerAuthentication.Sql,
    string? Login = null,
    string? Password = null,
    SqlServerEncryption Encryption = SqlServerEncryption.Required,
    string? Database = null)
{
    /// <summary>Keeps the password out of logs and debugger views, which print records by their members.</summary>
    /// <returns>The address only.</returns>
    public override string ToString() => $"ProbeInput {{ Address = {Address}, ServerId = {ServerId} }}";
}
