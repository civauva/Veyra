namespace Veyra.Access.Users;

/// <summary>
/// The outcome of a sign-in. It deliberately carries no reason for a failure (see <see cref="SignInService"/>).
/// </summary>
/// <param name="Succeeded">Whether the credentials were accepted.</param>
/// <param name="User">The signed-in user when <paramref name="Succeeded"/> is <see langword="true"/>.</param>
internal sealed record SignInResult(bool Succeeded, AccessUser? User)
{
    /// <summary>The result for every kind of failure.</summary>
    public static SignInResult Failed { get; } = new(false, null);
}
