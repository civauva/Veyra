namespace Veyra.Query.Server.Api;

/// <summary>A SQL Server event from system_health (ADR 0020, area 11).</summary>
/// <param name="At">When it happened.</param>
/// <param name="Name">The event's name, such as <c>error_reported</c>.</param>
/// <param name="Severity"><c>ok</c>, <c>caution</c>, or <c>warning</c>.</param>
/// <param name="Words">What it means, for anyone.</param>
/// <param name="Error">The error number, or 0.</param>
/// <param name="ErrorSeverity">SQL Server's severity, or 0.</param>
/// <param name="Wait">The wait type of a long wait, or empty.</param>
/// <param name="Message">SQL Server's message, or empty.</param>
public sealed record EventResponse(
    DateTimeOffset At,
    string Name,
    string Severity,
    string Words,
    int Error,
    int ErrorSeverity,
    string Wait,
    string Message);
