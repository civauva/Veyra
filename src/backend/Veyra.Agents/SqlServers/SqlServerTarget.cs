namespace Veyra.Agents.SqlServers;

/// <summary>A SQL Server together with its decrypted password, as the agent's configuration needs it.</summary>
/// <param name="Server">The server.</param>
/// <param name="Password">The login's password in plain text. Never logged, never returned by the REST API.</param>
internal sealed record SqlServerTarget(SqlServer Server, string Password)
{
    /// <summary>Keeps the password out of logs and debugger views, which print records by their members.</summary>
    /// <returns>The server's name.</returns>
    public override string ToString() => $"SqlServerTarget {{ Server = {Server.Name} }}";
}
