namespace Veyra.Core.Alerts;

/// <summary>
/// How serious an open alert is. The words and colours are the same on every screen (ADR 0007).
/// </summary>
public enum AlertSeverity
{
    /// <summary>Something needs attention, but users are not affected yet. Drawn amber.</summary>
    Caution = 1,

    /// <summary>Users are affected now. Drawn red.</summary>
    Warning = 2,
}
