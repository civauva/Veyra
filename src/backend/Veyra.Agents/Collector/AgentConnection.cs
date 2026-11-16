namespace Veyra.Agents.Collector;

/// <summary>How an agent's Collector reaches Veyra to send what it reads.</summary>
/// <param name="VeyraUrl">
/// Veyra's address as the agent reaches it, the same address it uses for OpAMP. OTLP over HTTP is served there too.
/// </param>
/// <param name="Key">The agent's ingestion key, which the Collector sends with every request.</param>
internal sealed record AgentConnection(Uri VeyraUrl, string Key)
{
    /// <summary>Keeps the key out of logs and debugger views, which print records by their members.</summary>
    /// <returns>The address only.</returns>
    public override string ToString() => $"AgentConnection {{ VeyraUrl = {VeyraUrl} }}";
}
