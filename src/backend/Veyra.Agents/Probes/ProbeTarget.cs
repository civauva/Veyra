using Veyra.Agents.SqlServers;

namespace Veyra.Agents.Probes;

/// <summary>A pending probe with its decrypted password, as the agent's configuration needs it (ADR 0028).</summary>
/// <param name="Id">The probe's ID, which its Collector components are named after.</param>
/// <param name="Connection">The connection it tests.</param>
/// <param name="Password">The password in plain text; empty for integrated authentication. Never logged.</param>
internal sealed record ProbeTarget(Guid Id, SqlServerConnection Connection, string Password)
{
    /// <summary>Keeps the password out of logs and debugger views, which print records by their members.</summary>
    /// <returns>The probe's ID.</returns>
    public override string ToString() => $"ProbeTarget {{ Id = {Id} }}";
}
