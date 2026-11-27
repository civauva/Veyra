using System.Text.RegularExpressions;
using Veyra.Agents.Agents;

namespace Veyra.Agents.SqlServers;

/// <summary>
/// Checks where and how an agent is to connect to a SQL Server, for a server that is saved and for a probe of one
/// that is not yet (ADR 0028), so both refuse the same input with the same words.
/// </summary>
internal static partial class SqlServerConnectionRules
{
    /// <summary>The port a server listens on unless it says otherwise.</summary>
    public const int DefaultPort = 1433;

    /// <summary>Checks a connection.</summary>
    /// <param name="platform">The kind of SQL Server.</param>
    /// <param name="address">
    /// The host name or IP address, or <c>HOST\INSTANCE</c>, which is split into the address and the instance.
    /// </param>
    /// <param name="port">The TCP port; <see langword="null"/> for 1433.</param>
    /// <param name="instance">The named instance, or <see langword="null"/>.</param>
    /// <param name="authentication">How the agent signs in.</param>
    /// <param name="login">The login; none for integrated authentication.</param>
    /// <param name="password">The password sent, if any, for its length; whether one is needed is the caller's.</param>
    /// <param name="encryption">How the connection is encrypted.</param>
    /// <param name="database">The database of an Azure SQL Database.</param>
    /// <param name="agent">What the agent that connects last reported, for its operating system.</param>
    /// <returns>The connection, trimmed.</returns>
    /// <exception cref="AgentsValidationException">The input breaks a rule.</exception>
    public static SqlServerConnection Check(
        SqlServerPlatform platform,
        string? address,
        int? port,
        string? instance,
        SqlServerAuthentication authentication,
        string? login,
        string? password,
        SqlServerEncryption encryption,
        string? database,
        AgentReport agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        Require(Enum.IsDefined(platform), "Choose the kind of SQL Server.");
        Require(Enum.IsDefined(encryption), "Choose how the connection is encrypted.");
        Require(Enum.IsDefined(authentication), "Choose how the agent signs in.");

        var host = address?.Trim() ?? "";
        var named = string.IsNullOrWhiteSpace(instance) ? null : instance.Trim();

        // HOST\INSTANCE, as SQL Server Management Studio writes it, is split: the address goes into the URL's host and
        // the instance into its path.
        if (host.IndexOf('\\', StringComparison.Ordinal) is var slash and >= 0)
        {
            var fromAddress = host[(slash + 1)..].Trim();

            Require(
                named is null || string.Equals(named, fromAddress, StringComparison.OrdinalIgnoreCase),
                @"Give the instance once: in the address as HOST\INSTANCE, or on its own.");

            host = host[..slash].Trim();
            named = fromAddress.Length == 0 ? null : fromAddress;
        }

        Require(
            host.Length <= 253 && AddressPattern().IsMatch(host),
            "Enter the server's host name or IP address, such as sql-prod-01.corp.local or 10.0.0.5.");

        var chosenPort = port ?? DefaultPort;
        Require(chosenPort is >= 1 and <= 65535, "The port must be from 1 to 65535.");

        if (named is not null)
        {
            Require(
                InstancePattern().IsMatch(named),
                "Name the instance with letters, digits, and underscores, at most 16, starting with a letter.");
            Require(
                platform == SqlServerPlatform.SqlServer,
                "Only a SQL Server that its owner runs has named instances.");
        }

        var name = login?.Trim() ?? "";

        switch (authentication)
        {
            case SqlServerAuthentication.Sql:
                Require(name.Length is > 0 and <= 128, "Enter the SQL login, at most 128 characters.");
                Require(
                    !name.Contains('\\', StringComparison.Ordinal),
                    @"A login with a backslash is a Windows login: choose Windows (NTLM) for DOMAIN\name.");
                break;

            case SqlServerAuthentication.Ntlm:
                Require(
                    name.Length <= 128 && WindowsLoginPattern().IsMatch(name),
                    @"Enter the Windows login as DOMAIN\name, at most 128 characters.");
                break;

            default:
                Require(
                    name.Length == 0 && string.IsNullOrEmpty(password),
                    "Integrated authentication signs in as the agent's service account: "
                    + "leave the login and password empty.");
                Require(
                    agent.IsWindows,
                    agent.OperatingSystem is null
                        ? "Integrated authentication needs an agent on Windows, "
                            + "and this agent has not said what it runs on yet."
                        : "Integrated authentication needs an agent on Windows. "
                            + "Choose Windows (NTLM) with a domain login instead.");
                break;
        }

        if (authentication != SqlServerAuthentication.Sql)
        {
            Require(
                platform == SqlServerPlatform.SqlServer,
                "Azure SQL signs in with SQL authentication: "
                + "Windows authentication is for a SQL Server its owner runs.");
        }

        Require(password is null || password.Length <= 128, "The password may have at most 128 letters.");

        var chosenDatabase = database?.Trim();

        if (platform == SqlServerPlatform.AzureSqlDatabase)
        {
            Require(
                !string.IsNullOrEmpty(chosenDatabase) && chosenDatabase.Length <= 128,
                "Name the database: Azure SQL Database is read one database at a time.");
        }
        else
        {
            Require(string.IsNullOrEmpty(chosenDatabase), "Only an Azure SQL Database names its database.");
            chosenDatabase = null;
        }

        return new SqlServerConnection(
            platform,
            host,
            chosenPort,
            named,
            authentication,
            name,
            encryption,
            chosenDatabase);
    }

    /// <summary>Whether a way of signing in needs a password.</summary>
    /// <param name="authentication">The way.</param>
    /// <returns>False only for integrated authentication.</returns>
    public static bool NeedsPassword(SqlServerAuthentication authentication) =>
        authentication != SqlServerAuthentication.Integrated;

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

    // SQL Server's rules for an instance name, without the rarely used characters that a URL would need escaped.
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,15}$")]
    private static partial Regex InstancePattern();

    // DOMAIN\name: one backslash, something on each side, no spaces at the ends.
    [GeneratedRegex(@"^[^\\\s][^\\]*\\[^\\]*[^\\\s]$")]
    private static partial Regex WindowsLoginPattern();
}
