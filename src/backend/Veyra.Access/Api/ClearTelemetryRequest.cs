namespace Veyra.Access.Api;

/// <summary>An Admin's request to clear all telemetry (VEY-59).</summary>
/// <param name="Confirm">The site's host name, typed by hand, such as <c>veyra.example.com</c>.</param>
public sealed record ClearTelemetryRequest(string? Confirm);
