using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Veyra.Alerting.Notifications;

/// <summary>
/// Sends the queued notifications that are due, and settles each one: sent, retried later, or failed for good.
/// </summary>
/// <param name="deliveries">The queue.</param>
/// <param name="sender">Sends to a channel.</param>
/// <param name="options">The Alerting settings with the batch size, the time limit, and the public address.</param>
/// <param name="clock">The current time, for due times and retries.</param>
/// <param name="logger">Receives every failed try.</param>
internal sealed partial class NotificationDispatcher(
    DeliveryStore deliveries,
    ChannelSender sender,
    IOptions<AlertingOptions> options,
    TimeProvider clock,
    ILogger<NotificationDispatcher> logger)
{
    /// <summary>
    /// The waits between tries. After the last one fails, the message has failed for good: six tries over about
    /// 25 minutes, long enough to ride out a restart of the receiver, short enough that the news is still news.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> RetryDelays =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
    ];

    /// <summary>Sends one batch of due messages.</summary>
    /// <param name="cancellationToken">Cancels the run when Veyra stops.</param>
    /// <returns>How many messages were tried.</returns>
    public async Task<int> DispatchAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();

        // The lease covers every try of the batch in turn, with room to spare, so nothing is claimed twice.
        var batch = settings.Notifications.BatchSize;
        var lease = (settings.Notifications.SendTimeout * (batch + 1)) + TimeSpan.FromMinutes(1);
        var due = await deliveries.ClaimDueAsync(now, batch, now + lease, cancellationToken);

        foreach (var delivery in due)
        {
            var result = delivery.Channel.Enabled
                ? await sender.SendAsync(
                    delivery.Channel,
                    NotificationMessages.For(delivery.Event, delivery.Alert, settings.PublicBaseUri()),
                    cancellationToken)
                : SendResult.Failed("The channel was switched off before the message went out.");

            await SettleAsync(delivery, result, delivery.Channel.Enabled, cancellationToken);
        }

        return due.Count;
    }

    private async Task SettleAsync(
        DueDelivery delivery,
        SendResult result,
        bool retry,
        CancellationToken cancellationToken)
    {
        var at = clock.GetUtcNow();

        if (result.Succeeded)
        {
            await deliveries.MarkSentAsync(delivery.Id, result.Status, at, cancellationToken);

            return;
        }

        var error = result.Error ?? "Unknown error";
        DateTimeOffset? next = retry && delivery.Attempt <= RetryDelays.Count
            ? at + RetryDelays[delivery.Attempt - 1]
            : null;

        await deliveries.MarkFailedAsync(delivery.Id, error, result.Status, at, next, cancellationToken);

        if (next is { } retryAt)
        {
            LogRetrying(delivery.Channel.Name, delivery.Alert.Subject.Name, delivery.Attempt, error, retryAt);
        }
        else
        {
            LogGaveUp(delivery.Channel.Name, delivery.Alert.Subject.Name, delivery.Attempt, error);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Notification to {Channel} about {Subject} failed on try {Attempt}: {Error}; retrying at {RetryAt}")]
    private partial void LogRetrying(string channel, string subject, int attempt, string error, DateTimeOffset retryAt);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Notification to {Channel} about {Subject} failed for good after {Attempt} tries: {Error}")]
    private partial void LogGaveUp(string channel, string subject, int attempt, string error);
}
