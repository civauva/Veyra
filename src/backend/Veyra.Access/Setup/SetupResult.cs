namespace Veyra.Access.Setup;

/// <summary>
/// The outcome of using the first-run setup link.
/// </summary>
public enum SetupResult
{
    /// <summary>The first administrator was created.</summary>
    Created = 1,

    /// <summary>The token is wrong, expired, or already used.</summary>
    InvalidToken = 2,

    /// <summary>Users already exist, so setup is no longer possible.</summary>
    AlreadyCompleted = 3,
}
