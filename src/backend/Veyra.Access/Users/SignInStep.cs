namespace Veyra.Access.Users;

/// <summary>Where a sign-in stands after a step (ADR 0011).</summary>
internal enum SignInStep
{
    /// <summary>It failed. Every reason looks the same to the caller.</summary>
    Failed,

    /// <summary>It succeeded; a session may start.</summary>
    SignedIn,

    /// <summary>The password was right; a code from the app or a recovery code comes next.</summary>
    NeedsCode,

    /// <summary>The password was right, but a second factor is required and must be set up first.</summary>
    NeedsSetup,
}
