namespace Veyra.Access.History;

/// <summary>How a sign-in attempt ended, as stored in <c>access.sign_in_event.outcome</c>.</summary>
public enum SignInOutcome
{
    /// <summary>Signed in with a password alone.</summary>
    SignedIn = 1,

    /// <summary>Signed in with a password and an app code.</summary>
    SignedInWithCode = 2,

    /// <summary>Signed in with a password and a recovery code.</summary>
    SignedInWithRecoveryCode = 3,

    /// <summary>The password was wrong.</summary>
    WrongPassword = 4,

    /// <summary>The app or recovery code was wrong.</summary>
    WrongCode = 5,

    /// <summary>The account was locked after too many wrong attempts.</summary>
    Locked = 6,

    /// <summary>The account is switched off.</summary>
    SwitchedOff = 7,
}
