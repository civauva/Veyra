using System.Net;

namespace Veyra.Alerting.Notifications;

/// <summary>The rules for webhook URLs, and the short hint that is shown instead of a stored URL.</summary>
internal static class WebhookUrls
{
    private const int _maxLength = 2000;

    // How much of the path the hint keeps. Enough to tell two Slack webhooks apart by workspace, not enough to use.
    private const int _hintPathLength = 12;

    /// <summary>Checks a URL a person entered and returns it parsed.</summary>
    /// <remarks>
    /// HTTPS only, because the URL and the message usually carry a token. Plain HTTP is allowed to this machine, for
    /// a local relay or a test receiver. Credentials in the URL are refused: they would end up in the hint.
    /// </remarks>
    /// <param name="url">The URL as entered.</param>
    /// <returns>The URL.</returns>
    /// <exception cref="AlertingValidationException">The URL breaks a rule.</exception>
    public static Uri Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new AlertingValidationException("Enter the webhook URL.");
        }

        if (url.Length > _maxLength)
        {
            throw new AlertingValidationException($"The webhook URL is longer than {_maxLength} characters.");
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp))
        {
            throw new AlertingValidationException("The webhook URL must be a full http or https address.");
        }

        if (parsed.Scheme == Uri.UriSchemeHttp && !IsThisMachine(parsed))
        {
            throw new AlertingValidationException("The webhook URL must use https, except for localhost.");
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            throw new AlertingValidationException(
                "Put credentials in the webhook's path or query, not before the host.");
        }

        return parsed;
    }

    /// <summary>
    /// Returns the start of a URL, safe to show, for example <c>https://hooks.slack.com/services/T0…</c>.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <returns>The hint.</returns>
    public static string Hint(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        var path = url.PathAndQuery;
        var shown = path.Length > _hintPathLength ? path[.._hintPathLength] + "…" : path;

        return $"{url.Scheme}://{url.Authority}{shown}";
    }

    private static bool IsThisMachine(Uri url) =>
        url.IsLoopback
        || string.Equals(url.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(url.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
}
