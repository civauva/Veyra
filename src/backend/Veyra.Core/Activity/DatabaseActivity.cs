namespace Veyra.Core.Activity;

/// <summary>
/// One record of what a database server is doing, read by Veyra's agent with a query of its own and kept in the store
/// for database activity rather than as a log record or a metric (ADR 0020).
/// </summary>
/// <remarks>
/// The agent sends these as log records named by a <c>veyra.record</c> attribute. Ingestion recognizes the name and
/// hands the record to <see cref="IDatabaseActivityWriter"/>; everything else stays a log record.
/// </remarks>
/// <param name="Block">The server's block, such as <c>microsoft.sql_server sql-prod-01</c> (ADR 0018).</param>
/// <param name="Environment">The server's environment, or <see langword="null"/> when its agent names none.</param>
/// <param name="SampledAt">When the server was read; every record of one read shares it.</param>
public abstract record DatabaseActivity(string Block, string? Environment, DateTimeOffset SampledAt);
