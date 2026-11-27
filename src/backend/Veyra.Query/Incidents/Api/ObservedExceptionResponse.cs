namespace Veyra.Query.Incidents.Api;

/// <summary>
/// The exception behind an incident (ADR 0026): the failing trace's cause, or the most frequent error message of the
/// affected services.
/// </summary>
/// <param name="Type">The exception's type, when known.</param>
/// <param name="Message">Its message.</param>
/// <param name="StackTrace">Its stack trace, when a span or record carries one.</param>
/// <param name="Service">The service that threw it.</param>
/// <param name="TraceId">The trace it happened in, when known.</param>
/// <param name="Source"><c>trace</c> or <c>logs</c>: where it was read from.</param>
public sealed record ObservedExceptionResponse(
    string? Type,
    string Message,
    string? StackTrace,
    string? Service,
    string? TraceId,
    string Source);
