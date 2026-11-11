using Veyra.Alerting.Notifications;

namespace Veyra.Alerting.Api;

/// <summary>The SMTP server email channels send through. It is set in the environment, not on the screen.</summary>
/// <param name="Configured">Whether a server is set, so email channels can send.</param>
/// <param name="Host">The server's host name.</param>
/// <param name="Port">The server's port.</param>
/// <param name="Security">How the connection is secured.</param>
/// <param name="From">The sender address.</param>
public sealed record EmailServerResponse(bool Configured, string? Host, int Port, SmtpSecurity Security, string? From);
