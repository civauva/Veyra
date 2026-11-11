namespace Veyra.Alerting.Api;

/// <summary>One point of the error-rate chart.</summary>
/// <param name="At">The end of the bucket: the moment the rule would have measured.</param>
/// <param name="Total">Records in the bucket.</param>
/// <param name="Errors">Error records in the bucket.</param>
/// <param name="WindowTotal">Records in the rule's window ending at <paramref name="At"/>.</param>
/// <param name="WindowErrors">Error records in that window.</param>
/// <param name="Ratio">
/// <paramref name="WindowErrors"/> over <paramref name="WindowTotal"/>, as the rule sees it; <see langword="null"/>
/// when the window holds no records.
/// </param>
public sealed record AlertPointResponse(
    DateTimeOffset At,
    long Total,
    long Errors,
    long WindowTotal,
    long WindowErrors,
    double? Ratio);
