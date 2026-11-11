namespace Veyra.Alerting.Notifications;

/// <summary>How one try to send a message ended.</summary>
/// <param name="Succeeded">Whether the channel accepted the message.</param>
/// <param name="Status">The HTTP status of a webhook's response, if there was one.</param>
/// <param name="Error">Why it failed, in words for an administrator; <see langword="null"/> on success.</param>
public sealed record SendResult(bool Succeeded, int? Status, string? Error)
{
    /// <summary>A failure without an HTTP status.</summary>
    /// <param name="error">Why it failed.</param>
    /// <returns>The result.</returns>
    public static SendResult Failed(string error) => new(false, null, error);
}
