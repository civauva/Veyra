namespace Veyra.Core.Security;

/// <summary>
/// Names of the authorization policies every module uses on its endpoints, for example
/// <c>[Authorize(Policy = VeyraPolicies.Editor)]</c>.
/// </summary>
/// <remarks>
/// A policy is met by its role and by every higher role. Endpoints without a policy still require a signed-in user,
/// because the fallback policy denies anonymous access by default.
/// </remarks>
public static class VeyraPolicies
{
    /// <summary>Requires at least the <see cref="VeyraRole.Viewer"/> role.</summary>
    public const string Viewer = "veyra.viewer";

    /// <summary>Requires at least the <see cref="VeyraRole.Editor"/> role.</summary>
    public const string Editor = "veyra.editor";

    /// <summary>Requires the <see cref="VeyraRole.Admin"/> role.</summary>
    public const string Admin = "veyra.admin";

    /// <summary>The claim type that carries a user's <see cref="VeyraRole"/> in the session.</summary>
    public const string RoleClaimType = "veyra.role";
}
