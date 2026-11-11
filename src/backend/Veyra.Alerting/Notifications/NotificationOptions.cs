namespace Veyra.Alerting.Notifications;

/// <summary>
/// Settings of alert notifications, bound from the <c>Veyra:Alerting:Notifications</c> section. Channels themselves
/// are kept in the database and managed on the Notifications screen.
/// </summary>
public sealed class NotificationOptions
{
    /// <summary>How often the dispatcher looks for messages to send. Defaults to 5 seconds.</summary>
    public TimeSpan DispatchInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The most messages one dispatcher run sends. Defaults to 10.</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>How long one try may take before it counts as failed. Defaults to 10 seconds.</summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The SMTP server for email channels, bound from <c>Veyra:Alerting:Notifications:Smtp</c>.</summary>
    public SmtpOptions Smtp { get; set; } = new();
}
