namespace Veyra.Alerting.Notifications;

/// <summary>Where one message to one channel stands. The numbers are stored in <c>alerting.delivery</c>.</summary>
public enum DeliveryState
{
    /// <summary>Not sent yet: waiting for its first or its next try.</summary>
    Pending = 0,

    /// <summary>The channel accepted it.</summary>
    Sent = 1,

    /// <summary>Every try failed; Veyra stopped trying.</summary>
    Failed = 2,
}
