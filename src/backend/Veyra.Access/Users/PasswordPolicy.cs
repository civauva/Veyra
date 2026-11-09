using Microsoft.Extensions.Options;

namespace Veyra.Access.Users;

/// <summary>
/// The rules a local password must meet (ADR 0008).
/// </summary>
/// <remarks>
/// Length is the only rule. Composition rules such as "one digit and one symbol" make passwords harder to remember
/// without making them much harder to guess, so Veyra asks for long passwords instead.
/// </remarks>
/// <param name="options">The Access settings, for the minimum length.</param>
internal sealed class PasswordPolicy(IOptions<AccessOptions> options)
{
    // Hashing is deliberately slow, so very long inputs are refused before they reach the hasher.
    private const int _maximumLength = 1024;

    /// <summary>
    /// Checks a proposed password.
    /// </summary>
    /// <param name="password">The password to check.</param>
    /// <returns><see langword="null"/> when the password is acceptable; otherwise what is wrong with it.</returns>
    public string? Check(string password)
    {
        var minimum = options.Value.MinimumPasswordLength;

        if (password.Length < minimum)
        {
            return $"The password needs at least {minimum} characters.";
        }

        if (password.Length > _maximumLength)
        {
            return $"The password can have at most {_maximumLength} characters.";
        }

        return null;
    }
}
