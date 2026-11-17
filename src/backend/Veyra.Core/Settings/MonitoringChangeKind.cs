namespace Veyra.Core.Settings;

/// <summary>What happened to a monitored system or agent.</summary>
public enum MonitoringChangeKind
{
    /// <summary>It was set up.</summary>
    Added,

    /// <summary>Some of its fields changed.</summary>
    Changed,

    /// <summary>It was removed.</summary>
    Removed,
}
