using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Access.Users;
using Veyra.Core.Security;

namespace Veyra.Access.Sessions;

/// <summary>
/// Builds the identity stored in the session cookie, and re-checks it against the database on every request.
/// </summary>
internal static class SessionPrincipal
{
    /// <summary>
    /// Creates the principal for a signed-in user.
    /// </summary>
    /// <param name="user">The user who signed in.</param>
    /// <returns>A principal with the user's ID, name, email, and role.</returns>
    public static ClaimsPrincipal Create(AccessUser user)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(VeyraPolicies.RoleClaimType, ((int)user.Role).ToString(CultureInfo.InvariantCulture)),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Reads the user ID from a principal.
    /// </summary>
    /// <param name="principal">The signed-in principal.</param>
    /// <returns>The user's ID.</returns>
    /// <exception cref="InvalidOperationException">The principal is not a Veyra session.</exception>
    public static Guid UserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("The request is not signed in to a Veyra session.");

    /// <summary>
    /// Reads the role from a principal.
    /// </summary>
    /// <param name="principal">The principal to inspect.</param>
    /// <returns>The role, or <see langword="null"/> when the principal has none.</returns>
    public static VeyraRole? Role(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(VeyraPolicies.RoleClaimType), CultureInfo.InvariantCulture, out var role)
            ? (VeyraRole)role
            : null;

    /// <summary>
    /// Re-checks a session cookie against the database. Wired to the cookie handler's <c>OnValidatePrincipal</c>.
    /// </summary>
    /// <remarks>
    /// A user who was disabled or deleted loses their session on the next request, and a changed role applies at
    /// once. The check costs one small query per request, which is cheap next to what the UI asks for anyway.
    /// </remarks>
    /// <param name="context">The cookie validation context.</param>
    /// <returns>A task that completes when the principal is accepted, replaced, or rejected.</returns>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal is null
            || !Guid.TryParse(context.Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            context.RejectPrincipal();

            return;
        }

        var store = context.HttpContext.RequestServices.GetRequiredService<UserStore>();
        var user = await store.FindByIdAsync(id, context.HttpContext.RequestAborted);

        if (user is null || user.Disabled)
        {
            context.RejectPrincipal();

            return;
        }

        if (context.Principal.Role() != user.Role || context.Principal.Identity?.Name != user.DisplayName)
        {
            context.ReplacePrincipal(Create(user));
            context.ShouldRenew = true;
        }
    }
}
