namespace Veyra.Alerting.Alerts;

/// <summary>What changed about an alert. The numbers are stored in <c>alerting.alert_event</c>.</summary>
public enum AlertEventKind
{
    /// <summary>The rule fired and the alert opened.</summary>
    Opened = 1,

    /// <summary>The alert rose from caution to warning.</summary>
    Escalated = 2,

    /// <summary>The alert fell from warning to caution but stays open.</summary>
    Eased = 3,

    /// <summary>The rule stopped firing and the alert closed.</summary>
    Resolved = 4,
}
