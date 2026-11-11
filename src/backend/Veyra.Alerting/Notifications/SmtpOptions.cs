namespace Veyra.Alerting.Notifications;

/// <summary>
/// The SMTP server that sends alert emails. It comes from the environment only (<c>VEYRA_SMTP_*</c> in
/// <c>deploy/.env</c>), never from the database, because it holds a password.
/// </summary>
public sealed class SmtpOptions
{
    /// <summary>The server's host name. Without one, email channels can be saved but cannot send.</summary>
    public string? Host { get; set; }

    /// <summary>The server's port. Defaults to 587, the submission port.</summary>
    public int Port { get; set; } = 587;

    /// <summary>How the connection is secured. Defaults to <see cref="SmtpSecurity.Auto"/>.</summary>
    public SmtpSecurity Security { get; set; } = SmtpSecurity.Auto;

    /// <summary>The user name to sign in with; leave it empty for a server that needs no sign-in.</summary>
    public string? Username { get; set; }

    /// <summary>The password that goes with <see cref="Username"/>.</summary>
    public string? Password { get; set; }

    /// <summary>The sender address, for example <c>Veyra &lt;veyra@example.com&gt;</c>. Required with a host.</summary>
    public string? From { get; set; }

    /// <summary>Whether a host is set, so email channels can send.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}
