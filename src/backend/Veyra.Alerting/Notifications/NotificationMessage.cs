namespace Veyra.Alerting.Notifications;

/// <summary>
/// What Veyra tells a channel. Webhooks receive it as JSON; email turns it into a subject and a body.
/// </summary>
/// <param name="Event">What happened: <c>opened</c>, <c>escalated</c>, <c>resolved</c>, or <c>test</c>.</param>
/// <param name="Text">One readable line. Chat tools that take a <c>text</c> field show it as it is.</param>
/// <param name="Alert">The alert it is about; <see langword="null"/> for a test message.</param>
internal sealed record NotificationMessage(string Event, string Text, AlertNotice? Alert);
