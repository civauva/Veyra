using Microsoft.AspNetCore.Http;

namespace Veyra.Access.Sessions;

/// <summary>Where a sign-in comes from: the client address and the browser.</summary>
/// <param name="Address">The address after the reverse proxy's forwarded headers, or <see langword="null"/>.</param>
/// <param name="UserAgent">The <c>User-Agent</c> header, cut to 512 characters, or <see langword="null"/>.</param>
internal sealed record SignInClient(string? Address, string? UserAgent)
{
    /// <summary>A client with nothing known, for sign-ins that do not come over HTTP.</summary>
    public static SignInClient Unknown { get; } = new(null, null);

    /// <summary>Reads the client of a request.</summary>
    /// <param name="context">The request.</param>
    /// <returns>The client.</returns>
    public static SignInClient From(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var userAgent = context.Request.Headers.UserAgent.ToString();
        var address = context.Connection.RemoteIpAddress;

        // Kestrel listens on both protocols, so IPv4 clients arrive as ::ffff:a.b.c.d; people know them as a.b.c.d.
        if (address is { IsIPv4MappedToIPv6: true })
        {
            address = address.MapToIPv4();
        }

        return new SignInClient(
            address?.ToString(),
            UserAgents.Trim(userAgent.Length == 0 ? null : userAgent));
    }
}
