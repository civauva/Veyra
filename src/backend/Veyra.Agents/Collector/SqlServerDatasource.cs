using System.Globalization;
using Veyra.Agents.SqlServers;

namespace Veyra.Agents.Collector;

/// <summary>
/// Writes the URL a Collector receiver connects to a SQL Server with, in go-mssqldb's form (ADR 0028). The template
/// writes it from variables for a SQL login and a port; Veyra writes it itself, since a named instance and Windows
/// authentication change its shape:
/// <list type="bullet">
/// <item><description>
/// <c>sqlserver://login:password@host:port?…</c> for a SQL login, the template's own form.
/// </description></item>
/// <item><description>
/// <c>sqlserver://login:password@host/INSTANCE?…</c> for a named instance: without a port, go-mssqldb asks the SQL
/// Server Browser on UDP 1434 for the instance's port.
/// </description></item>
/// <item><description>
/// <c>sqlserver://DOMAIN%5Cname:password@…</c> for Windows (NTLM): a user with a backslash signs in with Windows
/// authentication, NTLM on Linux and Windows' own SSPI with those credentials on Windows.
/// </description></item>
/// <item><description>
/// <c>sqlserver://host…</c> without a user for integrated authentication: on Windows, go-mssqldb then signs in as
/// the account the Collector runs as.
/// </description></item>
/// </list>
/// </summary>
internal static class SqlServerDatasource
{
    /// <summary>Writes the URL.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="password">The login's password; ignored for integrated authentication.</param>
    /// <returns>
    /// The URL, with every part escaped, so it holds no <c>$</c> the Collector would read as a variable.
    /// </returns>
    public static string For(SqlServerConnection connection, string password)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(password);

        var user = connection.Authentication == SqlServerAuthentication.Integrated
            ? ""
            : $"{Uri.EscapeDataString(connection.Login)}:{Uri.EscapeDataString(password)}@";
        var where = connection.Instance is { } instance
            ? $"{connection.Address}/{Uri.EscapeDataString(instance)}"
            : string.Create(CultureInfo.InvariantCulture, $"{connection.Address}:{connection.Port}");
        var encrypt = connection.Encryption == SqlServerEncryption.Off ? "false" : "true";
        var trust = connection.Encryption == SqlServerEncryption.TrustServerCertificate ? "true" : "false";
        var database = Uri.EscapeDataString(connection.Database ?? "master");

        return $"sqlserver://{user}{where}?encrypt={encrypt}&TrustServerCertificate={trust}"
            + $"&app+name=veyra-collector&database={database}";
    }
}
