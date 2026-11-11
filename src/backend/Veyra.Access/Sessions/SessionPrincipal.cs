using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Access.Users;
using Veyra.Core.Security;

namespace Veyra.Access.Sessions;

/// <summary>
/// Builds and reads the principal in the session cookie, and the short-lived one between the password and the second
/// factor (ADR 0008, ADR 0011).
/// </summary>
internal static class SessionPrincipal
{
    /// <summary>The authentication scheme of the cookie between the password step and the code step.</summary>
    public const string PendingScheme = "Veyra.Pending";

    /// <summary>The claim that carries the session ID in the session cookie.</summary>
    public const string SessionClaimType = "veyra.session";

    /// <summary>The claim that carries what the pending cookie allows: <c>code</c> or <c>setup</c>.</summary>
    public const string PendingStepClaimType = "veyra.pending";

    /// <summary>Builds the principal of a session.</summary>
    /// <param name="user">The signed-in user.</param>
    /// <param name="sessionId">The session's ID.</param>
    /// <returns>The principal for the session cookie.</returns>
    public static ClaimsPrincipal Create(AccessUser user, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(user);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(VeyraPolicies.RoleClaimType, ((int)user.Role).ToString(CultureInfo.InvariantCulture)),
                new Claim(SessionClaimType, sessionId),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        return new ClaimsPrincipal(identity);
    }

    /// <summary>Builds the principal of the pending cookie, which allows only the next sign-in step.</summary>
    /// <param name="user">The user whose password was right.</param>
    /// <param name="step"><c>code</c> or <c>setup</c>.</param>
    /// <returns>The principal for the pending cookie.</returns>
    public static ClaimsPrincipal CreatePending(AccessUser user, string step)
    {
        ArgumentNullException.ThrowIfNull(user);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(PendingStepClaimType, step),
            ],
            PendingScheme);

        return new ClaimsPrincipal(identity);
    }

    /// <summary>The user ID of a signed-in or pending principal.</summary>
    /// <param name="principal">The principal.</param>
    /// <returns>The ID.</returns>
    /// <exception cref="InvalidOperationException">The principal carries no user.</exception>
    public static Guid UserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("The request is not signed in to a Veyra session.");

    /// <summary>The session ID of a signed-in principal.</summary>
    /// <param name="principal">The principal.</param>
    /// <returns>The ID, or <see langword="null"/> for a principal without one.</returns>
    public static string? SessionId(this ClaimsPrincipal principal) => principal.FindFirstValue(SessionClaimType);

    /// <summary>What a pending principal allows next.</summary>
    /// <param name="principal">The principal.</param>
    /// <returns><c>code</c>, <c>setup</c>, or <see langword="null"/>.</returns>
    public static string? PendingStep(this ClaimsPrincipal principal) => principal.FindFirstValue(PendingStepClaimType);

    /// <summary>The role of a signed-in principal.</summary>
    /// <param name="principal">The principal.</param>
    /// <returns>The role, or <see langword="null"/>.</returns>
    public static VeyraRole? Role(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(VeyraPolicies.RoleClaimType), CultureInfo.InvariantCulture, out var role)
            ? (VeyraRole)role
            : null;

    /// <summary>
    /// Re-checks a session cookie against its session row and user. Wired to the cookie handler's
    /// <c>OnValidatePrincipal</c>.
    /// </summary>
    /// <remarks>
    /// A session that was ended, has been idle too long, or whose user was switched off is refused on the next
    /// request, and a changed role applies at once. Cookies from before sessions were recorded carry no session ID and
    /// are refused, so everyone signs in once after the upgrade. The check is one small query per request.
    /// </remarks>
    /// <param name="context">The cookie validation context.</param>
    /// <returns>A task that completes when the principal is accepted, replaced, or rejected.</returns>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal?.SessionId() is not { } sessionId)
        {
            context.RejectPrincipal();

            return;
        }

        var sessions = context.HttpContext.RequestServices.GetRequiredService<SessionService>();
        var user = await sessions.ValidateAsync(sessionId, context.HttpContext.RequestAborted);

        if (user is null || user.Id != context.Principal.UserId())
        {
            context.RejectPrincipal();

            return;
        }

        if (context.Principal.Role() != user.Role || context.Principal.Identity?.Name != user.DisplayName)
        {
            context.ReplacePrincipal(Create(user, sessionId));
            context.ShouldRenew = true;
        }
    }
}
