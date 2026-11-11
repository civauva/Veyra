namespace Veyra.Access.History;

/// <summary>One change to an account, as stored in <c>access.user_event</c>.</summary>
/// <param name="At">When it happened.</param>
/// <param name="Kind">What changed.</param>
/// <param name="ActorId">Who changed it, or <see langword="null"/> when Veyra did.</param>
/// <param name="ActorName">The display name of who changed it, or <see langword="null"/>.</param>
/// <param name="Detail">What it changed to, depending on the kind.</param>
internal sealed record UserEvent(
    DateTimeOffset At,
    UserEventKind Kind,
    Guid? ActorId,
    string? ActorName,
    string? Detail);
