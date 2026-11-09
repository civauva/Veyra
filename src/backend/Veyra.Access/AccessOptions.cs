namespace Veyra.Access;

/// <summary>
/// Settings of the Access module, bound from the <c>Veyra:Access</c> section. The defaults are the values decided in
/// ADR 0008.
/// </summary>
public sealed class AccessOptions
{
    /// <summary>
    /// Whether people may sign in with a local email and password. Turn it off once single sign-on works.
    /// </summary>
    public bool LocalSignInEnabled { get; set; } = true;

    /// <summary>How long a session stays valid without activity.</summary>
    public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromHours(8);

    /// <summary>The shortest password a local account may have.</summary>
    public int MinimumPasswordLength { get; set; } = 12;

    /// <summary>How many failed sign-ins in a row lock an account.</summary>
    public int LockoutThreshold { get; set; } = 5;

    /// <summary>How long a locked account stays locked.</summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long a valid ingestion key is remembered without asking the database. A revoked key keeps working for at
    /// most this long.
    /// </summary>
    public TimeSpan KeyCacheDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long the one-time setup link for the first administrator stays valid.</summary>
    public TimeSpan SetupTokenLifetime { get; set; } = TimeSpan.FromHours(24);
}
