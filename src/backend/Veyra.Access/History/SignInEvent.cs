namespace Veyra.Access.History;

/// <summary>One sign-in attempt, as stored in <c>access.sign_in_event</c>.</summary>
/// <param name="At">When it happened.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Address">The client address.</param>
/// <param name="UserAgent">The browser's <c>User-Agent</c> header.</param>
/// <param name="NewAddress">Whether the account had not signed in from this address within the kept history.</param>
internal sealed record SignInEvent(
    DateTimeOffset At,
    SignInOutcome Outcome,
    string? Address,
    string? UserAgent,
    bool NewAddress)
{
    /// <summary>Whether the attempt signed in.</summary>
    public bool Succeeded => Outcome is SignInOutcome.SignedIn
        or SignInOutcome.SignedInWithCode
        or SignInOutcome.SignedInWithRecoveryCode;
}
