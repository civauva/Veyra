namespace Veyra.Ingestion.Pipeline;

/// <summary>
/// What happened to the records of one OTLP request.
/// </summary>
/// <param name="Accepted">Records queued for storage.</param>
/// <param name="Rejected">Records refused for good, for example for an environment the key may not send to.</param>
/// <param name="RejectionReason">Why records were refused, returned to the sender as OTLP partial success.</param>
/// <param name="QueueFull">
/// <see langword="true"/> when nothing was queued because Veyra is busy. The sender should retry later.
/// </param>
internal sealed record IngestResult(int Accepted, int Rejected, string? RejectionReason, bool QueueFull);
