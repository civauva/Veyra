using Veyra.Access.Sessions;

namespace Veyra.Access.Users;

/// <summary>The outcome of a sign-in step.</summary>
/// <param name="Step">Where the sign-in stands.</param>
/// <param name="User">The user, for every step but <see cref="SignInStep.Failed"/>.</param>
/// <param name="Method">How the user signed in, when <see cref="Step"/> is <see cref="SignInStep.SignedIn"/>.</param>
internal sealed record SignInResult(SignInStep Step, AccessUser? User, SessionMethod Method = SessionMethod.Password)
{
    /// <summary>A failed step.</summary>
    public static SignInResult Failed { get; } = new(SignInStep.Failed, null);

    /// <summary>Whether a session may start.</summary>
    public bool Succeeded => Step == SignInStep.SignedIn;
}
