using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using Veyra.Core;
using Veyra.Core.Activity;
using Veyra.Core.Jobs;
using Veyra.Core.Maintenance;
using Veyra.Core.Metrics;
using Veyra.Core.Schema;
using Veyra.Core.Search;
using Veyra.Core.Settings;
using Veyra.Core.Telemetry;
using Veyra.Core.Traces;
using Veyra.Storage.Activity;
using Veyra.Storage.Ingest;
using Veyra.Storage.Jobs;
using Veyra.Storage.Logs;
using Veyra.Storage.Maintenance;
using Veyra.Storage.Metrics;
using Veyra.Storage.Schema;
using Veyra.Storage.Search;
using Veyra.Storage.Settings;
using Veyra.Storage.Traces;

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

        services.AddOptions<StorageOptions>().Bind(section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsRules>();

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
        services.AddSingleton<MetricPartitions>();
        services.AddSingleton<SpanPartitions>();
        services.AddSingleton<TraceRetention>();
        services.AddSingleton<ActivityPartitions>();
        services.AddSingleton<ActivityRetention>();
        services.AddSingleton<IngestCountPartitions>();
        services.AddSingleton<IngestCountRetention>();
        services.AddSingleton<JobRunPartitions>();
        services.AddSingleton<JobRunRetention>();

        if (configured)
        {
            services.AddSingleton<LogStore>();
            services.AddSingleton<ILogWriter>(provider => provider.GetRequiredService<LogStore>());
            services.AddSingleton<ILogSearch>(provider => provider.GetRequiredService<LogStore>());
            services.AddSingleton<ILogRates>(provider => provider.GetRequiredService<LogStore>());
            services.AddSingleton<IBlockEvents>(provider => provider.GetRequiredService<LogStore>());
            services.AddSingleton<ILogStorageUsage>(provider => provider.GetRequiredService<LogRetention>());
            services.AddSingleton<MetricStore>();
            services.AddSingleton<IMetricWriter>(provider => provider.GetRequiredService<MetricStore>());
            services.AddSingleton<IMetricSearch>(provider => provider.GetRequiredService<MetricStore>());
            services.AddSingleton<MetricRetention>();
            services.AddSingleton<SpanStore>();
            services.AddSingleton<ISpanWriter>(provider => provider.GetRequiredService<SpanStore>());
            services.AddSingleton<ITraceSearch>(provider => provider.GetRequiredService<SpanStore>());
            services.AddSingleton<ISignalSearch, SignalSearchStore>();
            services.AddSingleton<IIngestCounts, IngestCountStore>();
            services.AddSingleton<IJobRuns, JobRunStore>();
            services.AddSingleton<ActivityStore>();
            services.AddSingleton<IDatabaseActivityWriter>(provider => provider.GetRequiredService<ActivityStore>());
            services.AddSingleton<IDatabaseActivitySearch>(provider => provider.GetRequiredService<ActivityStore>());
            services.AddSingleton<IBackupHistory>(provider => provider.GetRequiredService<ActivityStore>());
            services.AddSingleton<MetricRollups>();
            services.AddSingleton<ITelemetryClearer, StorageClearer>();
            services.AddSingleton<LogCountBackfill>();
            services.AddSingleton<LogWordIndexBackfill>();
            services.AddSingleton<SchemaMigrator>();
            services.AddHostedService<StorageMaintenance>();
            services.AddHostedService<StorageRollups>();
        }
        else
        {
            // Other modules can still start; their first use of storage fails with a message that says what to set.
            services.AddSingleton<UnconfiguredLogStore>();
            services.AddSingleton<ILogWriter>(provider => provider.GetRequiredService<UnconfiguredLogStore>());
            services.AddSingleton<ILogSearch>(provider => provider.GetRequiredService<UnconfiguredLogStore>());
            services.AddSingleton<ILogRates>(provider => provider.GetRequiredService<UnconfiguredLogStore>());
            services.AddSingleton<IBlockEvents>(provider => provider.GetRequiredService<UnconfiguredLogStore>());
            services.AddSingleton<ILogStorageUsage>(provider => provider.GetRequiredService<UnconfiguredLogStore>());
            services.AddSingleton<UnconfiguredMetricStore>();
            services.AddSingleton<IMetricWriter>(provider => provider.GetRequiredService<UnconfiguredMetricStore>());
            services.AddSingleton<IMetricSearch>(provider => provider.GetRequiredService<UnconfiguredMetricStore>());
            services.AddSingleton<UnconfiguredSpanStore>();
            services.AddSingleton<ISpanWriter>(provider => provider.GetRequiredService<UnconfiguredSpanStore>());
            services.AddSingleton<ITraceSearch>(provider => provider.GetRequiredService<UnconfiguredSpanStore>());
            services.AddSingleton<ISignalSearch, UnconfiguredSignalSearch>();
            services.AddSingleton<IIngestCounts, UnconfiguredIngestCounts>();
            services.AddSingleton<IJobRuns, UnconfiguredJobRuns>();
            services.AddSingleton<UnconfiguredActivityStore>();
            services.AddSingleton<IDatabaseActivityWriter>(
                provider => provider.GetRequiredService<UnconfiguredActivityStore>());
            services.AddSingleton<IDatabaseActivitySearch>(
                provider => provider.GetRequiredService<UnconfiguredActivityStore>());
            services.AddSingleton<IBackupHistory>(
                provider => provider.GetRequiredService<UnconfiguredActivityStore>());
        }

        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                "storage",
                provider => new StorageHealthCheck(configured ? provider.GetRequiredService<NpgsqlDataSource>() : null),
                failureStatus: null,
                tags: [StorageHealthChecks.ReadinessTag]));

        // Retention and budgets on the Settings screen, with what a change would delete (ADR 0017).
        services.AddSingleton<IModuleSettings>(provider => new StorageSettings(
            configured
                ? new StorageFootprint(
                    provider.GetRequiredService<NpgsqlDataSource>(),
                    provider.GetRequiredService<TimeProvider>())
                : null,
            provider.GetServices<IValidateOptions<StorageOptions>>()));

        return services;
    }
}
