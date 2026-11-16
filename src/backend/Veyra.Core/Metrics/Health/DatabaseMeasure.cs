using Veyra.Core.Telemetry;

namespace Veyra.Core.Metrics.Health;

/// <summary>
/// One figure of a database server that a catalog rule judges (ADR 0020), such as how long requests have waited behind
/// one session. The Query module measures; the Alerting module compares with the rule's thresholds.
/// </summary>
/// <remarks>
/// A server that sends reads is measured on every evaluation, with 0 when nothing is wrong, so an open alert can
/// resolve. A figure about one part of a server, such as one database or one job, names it.
/// </remarks>
/// <param name="Server">The server's block, such as <c>microsoft.sql_server sql-prod-01</c>.</param>
/// <param name="Environment">The server's environment, or <see langword="null"/>.</param>
/// <param name="Measure">What is measured, as the catalog names it, such as <c>head-blocker-seconds</c>.</param>
/// <param name="Part">The part of the server it is about, or <see langword="null"/> for the server as a whole.</param>
/// <param name="Value">The figure, in the measure's unit.</param>
public sealed record DatabaseMeasure(
    TelemetryBlock Server,
    string? Environment,
    string Measure,
    string? Part,
    double Value);
