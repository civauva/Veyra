using System.Text.Json.Serialization;

namespace Veyra.Agents.Probes;

/// <summary>Why a connection probe failed (ADR 0028). Stored as its number.</summary>
public enum ProbeFailure
{
    /// <summary>No agent could run it: Veyra has no built-in agent, or the chosen one no longer exists.</summary>
    [JsonStringEnumMemberName("no-agent")]
    NoAgent = 1,

    /// <summary>The agent has not connected to Veyra for over 2 minutes, or never did.</summary>
    [JsonStringEnumMemberName("agent-offline")]
    AgentOffline = 2,

    /// <summary>The agent cannot reach the server: a wrong address, port, or instance, or a firewall.</summary>
    [JsonStringEnumMemberName("unreachable")]
    Unreachable = 3,

    /// <summary>SQL Server refused the login.</summary>
    [JsonStringEnumMemberName("login-failed")]
    LoginFailed = 4,

    /// <summary>The agent does not trust the server's certificate.</summary>
    [JsonStringEnumMemberName("certificate")]
    Certificate = 5,

    /// <summary>No answer and no error came within 2 minutes.</summary>
    [JsonStringEnumMemberName("timed-out")]
    TimedOut = 6,

    /// <summary>The connection failed for another reason, given with it.</summary>
    [JsonStringEnumMemberName("other")]
    Other = 7,
}
