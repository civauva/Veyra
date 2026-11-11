namespace Veyra.Access.SecondFactor;

/// <summary>A person's second factor, as stored in <c>access.user_second_factor</c>.</summary>
/// <param name="ProtectedSecret">The secret, encrypted with Data Protection.</param>
/// <param name="CreatedAt">When setup started.</param>
/// <param name="EnabledAt">When it was switched on, or <see langword="null"/> while it is being set up.</param>
/// <param name="LastUsedStep">The step of the last accepted code.</param>
/// <param name="RecoveryCodesLeft">Recovery codes not used yet.</param>
internal sealed record SecondFactorState(
    string ProtectedSecret,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EnabledAt,
    long? LastUsedStep,
    int RecoveryCodesLeft)
{
    /// <summary>Whether the second factor is asked for at sign-in.</summary>
    public bool Enabled => EnabledAt is not null;
}
