using Veyra.Access.Users;
using Veyra.Core.Security;

namespace Veyra.Access.SecondFactor;

/// <summary>Who the second-factor policy applies to (ADR 0011).</summary>
internal static class SecondFactorRules
{
    /// <summary>Whether a policy requires a second factor of a person.</summary>
    /// <param name="policy">The policy an Admin chose.</param>
    /// <param name="user">The person.</param>
    /// <returns>
    /// <see langword="true"/> when it does. People without a local password sign in through a provider, which is
    /// responsible for its own second factor, so they never need one here.
    /// </returns>
    public static bool Requires(this SecondFactorPolicy policy, AccessUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.PasswordHash is not null && policy switch
        {
            SecondFactorPolicy.Everyone => true,
            SecondFactorPolicy.Admins => user.Role == VeyraRole.Admin,
            _ => false,
        };
    }
}
