namespace Veyra.Alerting.Api;

/// <summary>
/// The incident's leading alert, its worst and of those the first, with its measurement against its threshold.
/// </summary>
/// <param name="AlertId">The alert's ID.</param>
/// <param name="Rule">Its rule.</param>
/// <param name="Value">Its latest measurement, in the rule's unit.</param>
/// <param name="Threshold">The threshold of its current severity.</param>
/// <param name="Peak">Its worst measurement.</param>
/// <param name="PeakAt">When the peak was measured, if known.</param>
public sealed record IncidentLeadingResponse(
    Guid AlertId,
    string Rule,
    double Value,
    double Threshold,
    double Peak,
    DateTimeOffset? PeakAt);
