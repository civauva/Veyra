using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Veyra.Core;
using Veyra.Core.Schema;
using Veyra.Core.Telemetry;
using Veyra.Storage.Logs;
using Veyra.Storage.Schema;

namespace Veyra.Storage;

/// <summary>
/// Registers the Storage module with the dependency injection container. <c>Veyra.Api</c> calls it at startup.
/// </summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>Registers the Storage module: PostgreSQL persistence, migrations, and retention.</summary>
    /// <remarks>
    /// When no connection string is configured, the module still registers, so the process can start and report the
    /// problem through <c>/readyz</c> instead of crashing. Migrations are then skipped.
    /// </remarks>
    /// <param name="services">The service collection to add the module to.</param>
    /// <param name="configuration">
    /// The application configuration. The module reads its <c>Veyra:Storage</c> section.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddVeyraStorage(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(VeyraOptionNames.Storage);

        services.AddOptions<StorageOptions>()
            .Bind(section)
            .Validate(o => o.LogRetentionDays >= 1, "Veyra:Storage:LogRetentionDays must be at least 1.")
            .Validate(
                o => o.MaxLogStorageGigabytes is null or > 0,
                "Veyra:Storage:MaxLogStorageGigabytes must be positive when set.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        // Read the value here, not through IOptions, because it decides which services exist at all.
        var connectionString = section[nameof(StorageOptions.ConnectionString)];

        var configured = !string.IsNullOrWhiteSpace(connectionString);

        // Always registered, so every module that needs the database can be constructed. Without a connection string
        // it fails only when it is actually used, with a message that says what to set.
        services.AddSingleton(_ => configured
            ? NpgsqlDataSource.Create(connectionString!)
            : throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage));

        services.AddSingleton<ISchemaMigrationSource, StorageMigrations>();
        services.AddSingleton<LogPartitions>();
        services.AddSingleton<LogRetention>();

        if (configured)
        {
            services.AddSingleton<LogStore>();
            services.AddSingleton<ILogWriter>(provider => provider.GetRequiredService<LogStore>());
            services.AddSingleton<ILogSearch>(provider => provider.GetRequiredService<LogStore>());
            services.AddSingleton<SchemaMigrator>();
            services.AddHostedService<LogPartitionMaintenance>();
        }
        else
        {
            // Other modules can still start; their first use of storage fails with a message that says what to set.
            services.AddSingleton<UnconfiguredLogStore>();
            services.AddSingleton<ILogWriter>(provider => provider.GetRequiredService<UnconfiguredLogStore>());
            services.AddSingleton<ILogSearch>(provider => provider.GetRequiredService<UnconfiguredLogStore>());
        }

        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                "storage",
                provider => new StorageHealthCheck(configured ? provider.GetRequiredService<NpgsqlDataSource>() : null),
                failureStatus: null,
                tags: [StorageHealthChecks.ReadinessTag]));

        return services;
    }
}
