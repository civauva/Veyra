namespace Veyra.Alerting.Catalog;

/// <summary>The unit a catalog rule's measure and thresholds are in (ADR 0020).</summary>
internal enum MeasureUnit
{
    /// <summary>Seconds; shown as a length of time, such as "3 min".</summary>
    Seconds,

    /// <summary>A share from 0 to 1; shown as a percentage.</summary>
    Ratio,

    /// <summary>A count of something; shown with the rule's noun, such as "3 requests".</summary>
    Count,

    /// <summary>Bytes; shown as MB or GB.</summary>
    Bytes,

    /// <summary>Days; shown as days.</summary>
    Days,

    /// <summary>1 when something is wrong, 0 when not; no thresholds to set.</summary>
    Flag,
}
