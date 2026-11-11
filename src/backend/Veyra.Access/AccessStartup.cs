using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Veyra.Access.SecondFactor;
using Veyra.Access.Setup;
using Veyra.Access.Users;

namespace Veyra.Access;

/// <summary>
/// What the Access module does once at startup, after the database is migrated.
/// </summary>
public static class AccessStartup
{
    /// <summary>
    /// Opens first-run setup when no user exists, and applies the server's way back in for a lost second factor
    /// when it is configured (ADR 0011).
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="cancellationToken">Cancels startup.</param>
    /// <returns>A task that completes when both are done.</returns>
    public static async Task PrepareVeyraAccessAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        await services.GetRequiredService<SetupService>().PrepareAsync(cancellationToken);

        if (services.GetRequiredService<IOptions<AccessOptions>>().Value.RemoveSecondFactor is { Length: > 0 } email)
        {
            await services.GetRequiredService<SecondFactorService>().RemoveForEmailAsync(
                services.GetRequiredService<UserStore>(),
                email,
                cancellationToken);
        }
    }
}
