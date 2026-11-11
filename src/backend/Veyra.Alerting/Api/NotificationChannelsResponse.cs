namespace Veyra.Alerting.Api;

/// <summary>Everything the Notifications screen shows: the email server and every channel.</summary>
/// <param name="EmailServer">The SMTP server from Veyra's settings, without its password.</param>
/// <param name="Channels">The channels, by name.</param>
public sealed record NotificationChannelsResponse(
    EmailServerResponse EmailServer,
    IReadOnlyList<ChannelResponse> Channels);
