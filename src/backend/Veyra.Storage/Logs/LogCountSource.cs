namespace Veyra.Storage.Logs;

/// <summary>Where a part of a counting window is counted from (ADR 0015).</summary>
internal enum LogCountSource
{
    /// <summary>The records themselves: the edges of a window inside a minute, and days not counted yet.</summary>
    Records,

    /// <summary>The counts per minute in <c>storage.log_count_minute</c>.</summary>
    Minutes,

    /// <summary>The counts per hour in <c>storage.log_count_hour</c>.</summary>
    Hours,

    /// <summary>
    /// The records of a day whose words are not indexed by hour yet (ADR 0016), matched against words the old way.
    /// </summary>
    UnindexedRecords,
}
