namespace Veyra.Storage.Logs;

/// <summary>
/// What one retention run removed.
/// </summary>
/// <param name="DroppedPartitions">
/// The daily partitions dropped, oldest first, for example <c>log_record_20261020</c>.
/// </param>
/// <param name="DeletedDefaultRows">Rows deleted from the default partition because they were too old.</param>
internal sealed record RetentionResult(IReadOnlyList<string> DroppedPartitions, long DeletedDefaultRows);
