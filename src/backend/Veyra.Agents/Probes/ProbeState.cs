using System.Text.Json.Serialization;

namespace Veyra.Agents.Probes;

/// <summary>Where a connection probe stands (ADR 0028). Stored as its number.</summary>
public enum ProbeState
{
    /// <summary>The agent has not answered yet.</summary>
    [JsonStringEnumMemberName("pending")]
    Pending = 0,

    /// <summary>The agent connected and read what the probe asks.</summary>
    [JsonStringEnumMemberName("succeeded")]
    Succeeded = 1,

    /// <summary>The probe could not run, or the connection failed; the failure says why.</summary>
    [JsonStringEnumMemberName("failed")]
    Failed = 2,
}
