using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Veyra.Access.Api;

/// <summary>
/// Answers <c>400 Bad Request</c> with a problem that asks for HTTPS when a session endpoint is called over plain
/// HTTP outside Development.
/// </summary>
/// <remarks>
/// Outside Development the session and antiforgery cookies are <c>Secure</c> (ADR 0008), so over plain HTTP a browser
/// drops the session cookie and the antiforgery system throws. Without this filter the person sees a successful
/// sign-in followed by a 500. It runs before model binding, so the password is never checked and a request that cannot
/// succeed never counts towards the account's lockout. Behind a reverse proxy, <c>X-Forwarded-Proto</c> decides,
/// because forwarded headers are applied before MVC runs.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireHttpsForSessionAttribute : Attribute, IResourceFilter
{
    /// <summary>The problem title the sign-in screen shows.</summary>
    public const string Title = "Signing in needs HTTPS. Open Veyra at its https:// address.";

    /// <summary>Stops the request with the HTTPS problem when it came over plain HTTP outside Development.</summary>
    /// <param name="context">The resource context supplied by MVC.</param>
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var http = context.HttpContext;

        if (http.Request.IsHttps || http.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            return;
        }

        context.Result = new ObjectResult(new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = Title })
        {
            StatusCode = StatusCodes.Status400BadRequest,
        };
    }

    /// <summary>Does nothing; the check happens before the action.</summary>
    /// <param name="context">The resource context supplied by MVC.</param>
    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
