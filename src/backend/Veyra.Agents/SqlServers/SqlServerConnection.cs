namespace Veyra.Agents.SqlServers;

/// <summary>
/// Where and how an agent connects to a SQL Server, already checked: what a saved server and a probe of an unsaved
/// one have in common (ADR 0028). The password is kept apart.
/// </summary>
/// <param name="Platform">The kind of SQL Server it is.</param>
/// <param name="Address">The host name or IP address.</param>
/// <param name="Port">The TCP port; not used with a named instance.</param>
/// <param name="Instance">
/// The named instance, found through the SQL Server Browser on UDP 1434; <see langword="null"/> for a port.
/// </param>
/// <param name="Authentication">How the agent signs in.</param>
/// <param name="Login">The login, <c>DOMAIN\name</c> for Windows (NTLM); empty for integrated authentication.</param>
/// <param name="Encryption">How the connection is encrypted.</param>
/// <param name="Database">The one database, for an Azure SQL Database; otherwise <see langword="null"/>.</param>
internal sealed record SqlServerConnection(
    SqlServerPlatform Platform,
    string Address,
    int Port,
    string? Instance,
    SqlServerAuthentication Authentication,
    string Login,
    SqlServerEncryption Encryption,
    string? Database);
