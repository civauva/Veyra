namespace Veyra.Alerting.Notifications;

/// <summary>How a notification channel delivers. The numbers are stored in <c>alerting.channel</c>.</summary>
public enum ChannelKind
{
    /// <summary>An HTTP POST of a JSON document to a URL.</summary>
    Webhook = 1,

    /// <summary>An email to a list of recipients, through the SMTP server in Veyra's settings.</summary>
    Email = 2,
}
