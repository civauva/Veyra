using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Veyra.Storage.Logs;
using Veyra.Storage.Schema;

namespace Veyra.Storage;

/// <summary>
/// Work the Storage module must finish before Veyra starts serving requests.
/// </summary>
public static partial class StorageStartup
{
    /// <summary>
    /// Applies all pending schema migrations and creates the current log partitions. <c>Veyra.Api</c> calls it after
    /// building the app and before it starts listening, so no request ever sees an old schema.
    /// </summary>
    /// <param name="services">The application's service provider.</param>
    /// <param name="cancellationToken">Cancels the migration, for example when the process is asked to stop.</param>
    /// <returns>
    /// <see langword="true"/> when the schema is up to date; <see langword="false"/> when storage is not configured
    /// and nothing was done.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static async Task<bool> MigrateVeyraStorageAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        var migrator = services.GetService<SchemaMigrator>();

        if (migrator is null)
        {
            // Not configured: start anyway and let /readyz report it, which is easier to diagnose than a crash loop.
            LogNotConfigured(services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(StorageStartup)));

            return false;
        }

        await migrator.MigrateAsync(cancellationToken);

        // The first partitions must exist before ingestion starts; the background service keeps them ahead later.
        await services.GetRequiredService<LogPartitions>().EnsureAsync(cancellationToken);

        return true;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Storage is not configured; skipping database migrations. Set Veyra__Storage__ConnectionString")]
    private static partial void LogNotConfigured(ILogger logger);
}
