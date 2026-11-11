using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Veyra.Alerting.Notifications;

/// <summary>
/// Encrypts webhook URLs before they are stored, with ASP.NET Data Protection and the key ring that also protects
/// session cookies (the <c>veyra-keys</c> volume).
/// </summary>
/// <param name="provider">The Data Protection system.</param>
internal sealed class WebhookUrlProtector(IDataProtectionProvider provider)
{
    // The purpose isolates these payloads from cookies and anything else protected with the same keys.
    private readonly IDataProtector _protector = provider.CreateProtector("Veyra.Alerting.WebhookUrl.v1");

    /// <summary>Encrypts a URL for storage.</summary>
    /// <param name="url">The URL.</param>
    /// <returns>The encrypted text.</returns>
    public string Protect(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        return _protector.Protect(url.ToString());
    }

    /// <summary>Decrypts a stored URL.</summary>
    /// <param name="protectedUrl">The encrypted text.</param>
    /// <returns>The URL, or <see langword="null"/> when it cannot be read, because the key ring was replaced.</returns>
    public Uri? Unprotect(string protectedUrl)
    {
        try
        {
            return new Uri(_protector.Unprotect(protectedUrl), UriKind.Absolute);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
