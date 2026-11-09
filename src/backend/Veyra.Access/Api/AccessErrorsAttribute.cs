using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Veyra.Access.Api;

/// <summary>
/// Turns the Access module's exceptions into problem details (RFC 9457): validation errors become
/// <c>400 Bad Request</c>, conflicts become <c>409 Conflict</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AccessErrorsAttribute : ExceptionFilterAttribute
{
    /// <summary>Maps a known exception to its status code; leaves every other exception alone.</summary>
    /// <param name="context">The exception context supplied by MVC.</param>
    public override void OnException(ExceptionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var status = context.Exception switch
        {
            AccessValidationException => StatusCodes.Status400BadRequest,
            AccessConflictException => StatusCodes.Status409Conflict,
            _ => (int?)null,
        };

        if (status is null)
        {
            return;
        }

        context.Result = new ObjectResult(new ProblemDetails { Status = status, Title = context.Exception.Message })
        {
            StatusCode = status,
        };
        context.ExceptionHandled = true;
    }
}
