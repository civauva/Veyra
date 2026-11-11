namespace Veyra.Alerting.Notifications;

/// <summary>What a notified alert is about (ADR 0012).</summary>
/// <param name="Kind"><c>service</c> or <c>host</c>.</param>
/// <param name="Name">The service or host name.</param>
/// <param name="Part">The part of the block, for example a disk's mount point; <see langword="null"/> for none.</param>
internal sealed record BlockNotice(string Kind, string Name, string? Part);
