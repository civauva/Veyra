namespace Veyra.Agents.SqlServers;

/// <summary>A SQL Server as the Settings screen sends it, to add one or to change one.</summary>
/// <param name="Name">
/// Its name in Veyra, such as <c>sql-prod-01</c>: letters, digits, dots, dashes, and underscores, at most 63.
/// </param>
/// <param name="Address">The host name or IP address the agent connects to.</param>
/// <param name="Port">The TCP port, from 1 to 65535.</param>
/// <param name="Environment">The environment its telemetry is filed under, such as <c>production</c>.</param>
/// <param name="AgentId">The agent that reads it; <see langword="null"/> for the built-in agent.</param>
/// <param name="Login">The SQL login the agent signs in with.</param>
/// <param name="Password">
/// The login's password. Required to add a server; <see langword="null"/> when changing one keeps the stored password.
/// </param>
/// <param name="Encryption">How the connection is encrypted.</param>
public sealed record SqlServerInput(
    string Name,
    string Address,
    int Port,
    string Environment,
    Guid? AgentId,
    string Login,
    string? Password,
    SqlServerEncryption Encryption)
{
    /// <summary>Keeps the password out of logs and debugger views, which print records by their members.</summary>
    /// <returns>The server's name and address.</returns>
    public override string ToString() => $"SqlServerInput {{ Name = {Name}, Address = {Address}:{Port} }}";
}
