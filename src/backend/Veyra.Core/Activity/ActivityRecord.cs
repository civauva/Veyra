namespace Veyra.Core.Activity;

/// <summary>
/// A record of database activity other than the live view's sessions (ADR 0020): a query plan, a deadlock, an event,
/// or a snapshot of settings, kept as the agent sent it, by kind and key.
/// </summary>
/// <param name="Block">The server's block.</param>
/// <param name="Environment">The server's environment.</param>
/// <param name="SampledAt">When the server was read.</param>
/// <param name="Kind">What it is, such as <c>sqlserver.plan</c>.</param>
/// <param name="Key">What identifies it within its kind, such as a plan hash; empty when nothing does.</param>
/// <param name="Attributes">The row's other columns as text.</param>
/// <param name="Body">Its large text, such as a plan's XML; empty when it has none.</param>
public sealed record ActivityRecord(
    string Block,
    string? Environment,
    DateTimeOffset SampledAt,
    string Kind,
    string Key,
    IReadOnlyDictionary<string, string> Attributes,
    string Body) : DatabaseActivity(Block, Environment, SampledAt);
