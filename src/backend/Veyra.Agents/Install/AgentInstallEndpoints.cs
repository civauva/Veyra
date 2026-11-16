using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Veyra.Agents.Agents;
using Veyra.Agents.Enrollment;

namespace Veyra.Agents.Install;

/// <summary>
/// What an installer in another network talks to (VEY-40): the installers themselves, and the endpoint that exchanges
/// a one-time code for the agent's start-up file. Like OpAMP, they are open to anonymous callers: the code is the
/// credential.
/// </summary>
internal static class AgentInstallEndpoints
{
    // A code and a platform name: a few dozen bytes.
    private const int _maxEnrollBytes = 1024;

    /// <summary>
    /// Maps <c>GET /v1/agents/install.ps1</c>, <c>/v1/agents/install.sh</c>, and <c>POST /v1/agents/enroll</c>.
    /// </summary>
    /// <param name="endpoints">The application's endpoint builder.</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/agents/install.ps1", (HttpContext context) =>
                Results.Text(InstallScripts.Windows(VeyraUrl(context.Request)), "text/plain; charset=utf-8"))
            .AllowAnonymous()
            .WithName("AgentInstallWindows");

        endpoints.MapGet("/v1/agents/install.sh", (HttpContext context) =>
                Results.Text(InstallScripts.Linux(VeyraUrl(context.Request)), "text/x-shellscript; charset=utf-8"))
            .AllowAnonymous()
            .WithName("AgentInstallLinux");

        endpoints.MapPost("/v1/agents/enroll", EnrollAsync)
            .AllowAnonymous()
            .WithMetadata(new RequestSizeLimitAttribute(_maxEnrollBytes))
            .WithName("AgentEnroll");
    }

    private static async Task<IResult> EnrollAsync(
        HttpContext context,
        EnrollRequest request,
        AgentEnrollment enrollment,
        EnrollThrottle throttle,
        CancellationToken cancellationToken)
    {
        var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (throttle.IsBlocked(client))
        {
            context.Response.Headers.RetryAfter = "60";

            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        if (!Enum.TryParse<AgentPlatform>(request.Platform, ignoreCase: true, out var platform)
            || platform == AgentPlatform.Container)
        {
            return Results.Problem(title: "The platform must be Windows or Linux.", statusCode: 400);
        }

        var enrolled = await enrollment.EnrollAsync(
            request.Code ?? "",
            platform,
            VeyraUrl(context.Request),
            cancellationToken);

        if (enrolled is null)
        {
            throttle.RecordFailure(client);

            return Results.Problem(
                title: "The code is wrong, used, or older than an hour. Make a new one under Settings → Agents.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        // The start-up file itself, so a shell script can write it without a JSON reader.
        return Results.Text(enrolled.SupervisorConfig, "text/yaml; charset=utf-8");
    }

    // The address the machine used, which its agent will use too: through the reverse proxy for a remote one.
    private static Uri VeyraUrl(HttpRequest request) => new($"{request.Scheme}://{request.Host}");

    /// <summary>What an installer sends.</summary>
    /// <param name="Code">The one-time code.</param>
    /// <param name="Platform"><c>Windows</c> or <c>Linux</c>.</param>
    internal sealed record EnrollRequest(string? Code, string? Platform)
    {
        /// <summary>Keeps the code out of logs.</summary>
        /// <returns>The platform only.</returns>
        public override string ToString() => $"EnrollRequest {{ Platform = {Platform} }}";
    }
}
