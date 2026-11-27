using System.Text.Json.Serialization;

namespace Veyra.Agents.SqlServers;

/// <summary>How the agent signs in to a SQL Server (ADR 0028). Stored as its number.</summary>
public enum SqlServerAuthentication
{
    /// <summary>A SQL login and its password.</summary>
    [JsonStringEnumMemberName("sql")]
    Sql = 1,

    /// <summary>
    /// A Windows domain login, <c>DOMAIN\name</c>, and its password, from an agent on any system: NTLM on Linux,
    /// Windows' own sign-in with those credentials on Windows.
    /// </summary>
    [JsonStringEnumMemberName("ntlm")]
    Ntlm = 2,

    /// <summary>
    /// The Windows account the agent's service runs as, without a login or password. Windows agents only.
    /// </summary>
    [JsonStringEnumMemberName("integrated")]
    Integrated = 3,
}
