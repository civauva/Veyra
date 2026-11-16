namespace Veyra.Query.Storage.Api;

/// <summary>A volume that holds database files, with when it fills at its pace of the last week (ADR 0020).</summary>
/// <param name="Volume">The volume's mount point.</param>
/// <param name="SizeBytes">Its size.</param>
/// <param name="FreeBytes">Its free space.</param>
/// <param name="UsedRatio">The share in use.</param>
/// <param name="DaysUntilFull">
/// Days until full at the pace of the last week, or <see langword="null"/> when not filling.
/// </param>
public sealed record VolumeResponse(
    string Volume,
    double SizeBytes,
    double FreeBytes,
    double UsedRatio,
    double? DaysUntilFull);
