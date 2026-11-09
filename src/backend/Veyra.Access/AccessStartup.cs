using Microsoft.Extensions.DependencyInjection;
using Veyra.Access.Setup;

namespace Veyra.Access;

/// <summary>
/// Work the Access module must finish before Veyra starts serving requests.
/// </summary>
public static class AccessStartup
{
    /// <summary>
    /// Opens first-run setup when no user exists yet, which writes the one-time setup link to the log. Call it only
    /// after the database migrations have run, because it reads the users table.
    /// </summary>
    /// <param name="services">The application's service provider.</param>
    /// <param name="cancellationToken">Cancels the check, for example when the process is asked to stop.</param>
    /// <returns>A task that completes when setup is opened or known to be done.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static async Task PrepareVeyraAccessAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        await services.GetRequiredService<SetupService>().PrepareAsync(cancellationToken);
    }
}
