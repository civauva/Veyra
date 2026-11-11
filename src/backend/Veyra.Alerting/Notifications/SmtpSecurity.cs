namespace Veyra.Alerting.Notifications;

/// <summary>How the connection to the SMTP server is secured.</summary>
public enum SmtpSecurity
{
    /// <summary>TLS from the start on port 465, otherwise STARTTLS when the server offers it.</summary>
    Auto,

    /// <summary>Plain connection upgraded with STARTTLS; fails if the server does not offer it.</summary>
    StartTls,

    /// <summary>TLS from the start, usually on port 465.</summary>
    SslOnConnect,

    /// <summary>No encryption. Only for a relay on the same machine or a test server.</summary>
    None,
}
