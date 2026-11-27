namespace Veyra.Agents.Collector;

/// <summary>
/// One read an agent runs for a SQL Server, as the integration page's active checks list it (ADR 0028).
/// </summary>
/// <param name="Name">The read's name, from the <c>/* veyra-agent: name */</c> that starts its SQL.</param>
/// <param name="Module">The module it belongs to, such as <c>storage</c>.</param>
/// <param name="Receiver">The template receiver it runs in, such as <c>sqlquery/storage</c>.</param>
internal sealed record CollectorRead(string Name, string Module, string Receiver);
