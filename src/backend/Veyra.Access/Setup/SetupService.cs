using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Veyra.Access.Users;
using Veyra.Core.Security;

namespace Veyra.Access.Setup;

/// <summary>
/// Creates the first administrator through a one-time link written to the log (ADR 0008).
/// </summary>
/// <remarks>
/// <para>
/// Only someone who can read the server's log can open the link, which is the same trust as having the server.
/// There is no default password and no administrator password in environment variables.
/// </para>
/// <para>
/// The token lives only in memory. A restart before setup issues a new token and the old one stops working.
/// </para>
/// </remarks>
/// <param name="store">Where users are stored.</param>
/// <param name="hasher">The PBKDF2 password hasher.</param>
/// <param name="policy">The password rules.</param>
/// <param name="options">The Access settings, for the token lifetime.</param>
/// <param name="clock">The clock for token expiry.</param>
/// <param name="logger">Receives the setup link and the security events.</param>
internal sealed partial class SetupService(
    UserStore store,
    IPasswordHasher<AccessUser> hasher,
    PasswordPolicy policy,
    IOptions<AccessOptions> options,
    TimeProvider clock,
    ILogger<SetupService> logger)
{
    private readonly Lock _gate = new();

    private DateTimeOffset _expiresAt;

    /// <summary>The current setup token, or <see langword="null"/> when setup is not open.</summary>
    internal string? Token { get; private set; }

    /// <summary>
    /// Whether Veyra still needs its first administrator.
    /// </summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><see langword="true"/> while no user exists.</returns>
    public async Task<bool> IsRequiredAsync(CancellationToken cancellationToken) =>
        await store.CountAsync(cancellationToken) == 0;

    /// <summary>
    /// Opens setup when no user exists: issues a token and writes the setup link to the log. Called once at startup.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A task that completes when the token is issued or setup is known to be done.</returns>
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (!await IsRequiredAsync(cancellationToken))
        {
            return;
        }

        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var lifetime = options.Value.SetupTokenLifetime;

        lock (_gate)
        {
            Token = token;
            _expiresAt = clock.GetUtcNow() + lifetime;
        }

        LogSetupOpen(token, lifetime);
    }

    /// <summary>
    /// Creates the first administrator when the token is right.
    /// </summary>
    /// <param name="token">The token from the setup link.</param>
    /// <param name="email">The administrator's email address.</param>
    /// <param name="displayName">The administrator's display name.</param>
    /// <param name="password">The administrator's password.</param>
    /// <param name="cancellationToken">Cancels the setup.</param>
    /// <returns>Whether the administrator was created, and if not, why.</returns>
    /// <exception cref="AccessValidationException">The email or password is not acceptable.</exception>
    public async Task<SetupResult> CompleteAsync(
        string token,
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken)
    {
        if (!IsCurrentToken(token))
        {
            LogSetupRejected();

            return SetupResult.InvalidToken;
        }

        var admin = UserService.NewUser(email, displayName, VeyraRole.Admin, password, policy, hasher, clock);

        if (!await store.InsertFirstAdminAsync(admin, cancellationToken))
        {
            CloseSetup();

            return SetupResult.AlreadyCompleted;
        }

        CloseSetup();
        LogSetupCompleted(admin.Id);

        return SetupResult.Created;
    }

    private bool IsCurrentToken(string candidate)
    {
        lock (_gate)
        {
            if (Token is null || clock.GetUtcNow() >= _expiresAt)
            {
                return false;
            }

            // Constant-time comparison, so the response time does not reveal how much of a guess was right.
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(Token),
                Encoding.UTF8.GetBytes(candidate));
        }
    }

    private void CloseSetup()
    {
        lock (_gate)
        {
            Token = null;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "First-run setup: open https://<your host>/setup?token={Token} within {Lifetime} to create the "
            + "first administrator. The link works once.")]
    private partial void LogSetupOpen(string token, TimeSpan lifetime);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Security: first-run setup rejected an invalid or expired token")]
    private partial void LogSetupRejected();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Security: first-run setup created administrator {UserId}")]
    private partial void LogSetupCompleted(Guid userId);
}
