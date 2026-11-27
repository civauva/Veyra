namespace Veyra.Alerting.Api;

/// <summary>One error a database server's error alert counted (VEY-61).</summary>
/// <param name="At">When the server was read with it, within a minute of when it happened.</param>
/// <param name="Error">SQL Server's error number, such as <c>824</c>.</param>
/// <param name="Severity">Its severity, from 0 to 25.</param>
/// <param name="Message">What SQL Server said, or what Veyra says of an event without a message.</param>
public sealed record ServerErrorResponse(DateTimeOffset At, int Error, int Severity, string Message);
