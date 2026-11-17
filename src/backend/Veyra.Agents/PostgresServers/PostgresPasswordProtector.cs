using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Veyra.Agents.PostgresServers;

/// <summary>
/// Encrypts PostgreSQL passwords before they are stored, with ASP.NET Data Protection and the key ring that also
/// protects session cookies, webhook URLs, and SQL Server passwords (the <c>veyra-keys</c> volume).
/// </summary>
/// <param name="provider">The Data Protection system.</param>
internal sealed class PostgresPasswordProtector(IDataProtectionProvider provider)
{
    // Its own purpose, so a PostgreSQL password cannot be passed off as anything else protected with the same keys.
    private readonly IDataProtector _protector = provider.CreateProtector("Veyra.Agents.PostgresServerPassword.v1");

    /// <summary>Encrypts a password for storage.</summary>
    /// <param name="password">The password.</param>
    /// <returns>The encrypted text.</returns>
    public string Protect(string password) => _protector.Protect(password);

    /// <summary>Decrypts a stored password.</summary>
    /// <param name="protectedPassword">The encrypted text.</param>
    /// <returns>
    /// The password, or <see langword="null"/> when it cannot be read, because the key ring was replaced.
    /// </returns>
    public string? Unprotect(string protectedPassword)
    {
        try
        {
            return _protector.Unprotect(protectedPassword);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
