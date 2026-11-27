namespace Veyra.Query.Backups.Api;

/// <summary>One backup as msdb recorded it (ADR 0025).</summary>
/// <param name="Database">The database backed up.</param>
/// <param name="Type"><c>full</c>, <c>differential</c>, or <c>log</c>.</param>
/// <param name="StartedAt">When it started.</param>
/// <param name="FinishedAt">When it finished.</param>
/// <param name="Seconds">How long it took.</param>
/// <param name="Bytes">Its size before compression; <see langword="null"/> when msdb recorded none.</param>
/// <param name="CompressedBytes">Its size as written; <see langword="null"/> when msdb recorded none.</param>
/// <param name="CopyOnly">Whether it was copy-only, which leaves the backup chain as it was.</param>
/// <param name="Device">Where it was written, such as a file or a URL; <see langword="null"/> when not known.</param>
public sealed record BackupResponse(
    string Database,
    string Type,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    double Seconds,
    long? Bytes,
    long? CompressedBytes,
    bool CopyOnly,
    string? Device);
