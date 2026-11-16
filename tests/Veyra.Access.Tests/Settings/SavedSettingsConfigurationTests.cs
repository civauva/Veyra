using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Veyra.Access.Settings;
using Veyra.Testing;

namespace Veyra.Access.Tests.Settings;

[Collection(PostgresGroup.Name)]
public class SavedSettingsConfigurationTests(PostgresFixture postgres)
{
    private const string _days = "Veyra:Storage:LogRetentionDays";

    private const string _password = "Veyra:Alerting:Notifications:Smtp:Password";

    [Fact]
    public async Task Load_SavedValue_WinsOverTheEnvironment()
    {
        var connectionString = await MigratedAsync();
        await SaveAsync(connectionString, new SettingWrite(_days, "7", false));

        var configuration = Build(connectionString, (_days, "30"));

        Assert.Equal("7", configuration[_days]);
    }

    [Fact]
    public async Task Load_NothingSaved_LeavesTheEnvironmentsValue()
    {
        var configuration = Build(await MigratedAsync(), (_days, "30"));

        Assert.Equal("30", configuration[_days]);
    }

    [Fact]
    public async Task Load_SchemaNotYetMigrated_StartsWithNothingSaved()
    {
        var configuration = Build(await postgres.CreateDatabaseConnectionStringAsync(), (_days, "30"));

        Assert.Equal("30", configuration[_days]);
    }

    [Fact]
    public async Task Activate_Secret_IsReadOnlyOnceItCanBeDecrypted()
    {
        var connectionString = await MigratedAsync();
        var protection = new EphemeralDataProtectionProvider();
        await SaveAsync(connectionString, new SettingWrite(_password, "longer secret", true), protection);
        var configuration = Build(connectionString);
        var runtime = new SavedSettingsRuntime(configuration);

        Assert.Null(configuration[_password]);

        runtime.Activate(protection);

        Assert.Equal("longer secret", configuration[_password]);
    }

    [Fact]
    public async Task Refresh_AfterASave_ReachesTheOptionsMonitor()
    {
        var connectionString = await MigratedAsync();
        var configuration = Build(connectionString, (_days, "14"));
        var runtime = new SavedSettingsRuntime(configuration);
        var monitor = Monitor(configuration);
        Assert.Equal(14, monitor.CurrentValue.LogRetentionDays);

        await SaveAsync(connectionString, new SettingWrite(_days, "5", false));
        runtime.Refresh();

        Assert.Equal(5, monitor.CurrentValue.LogRetentionDays);
    }

    [Fact]
    public async Task Version_FollowsTheLastSave()
    {
        var connectionString = await MigratedAsync();
        var runtime = new SavedSettingsRuntime(Build(connectionString));
        Assert.Equal(0, runtime.Version);

        var saved = await SaveAsync(connectionString, new SettingWrite(_days, "5", false));
        runtime.Refresh();

        Assert.Equal(saved, runtime.Version);
    }

    private Task<string> MigratedAsync() =>
        postgres.CreateMigratedDatabaseConnectionStringAsync(new AccessMigrations());

    private static IConfigurationRoot Build(string connectionString, params (string Key, string Value)[] environment) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(environment.Select(value => KeyValuePair.Create(value.Key, (string?)value.Value)))
            .Add(new SavedSettingsConfigurationSource(connectionString))
            .Build();

    private static async Task<long> SaveAsync(
        string connectionString,
        SettingWrite write,
        IDataProtectionProvider? protection = null)
    {
        await using var database = NpgsqlDataSource.Create(connectionString);
        var store = new SettingStore(
            database,
            protection ?? new EphemeralDataProtectionProvider(),
            TimeProvider.System);
        var version = await store.VersionAsync(default);

        return (await store.SaveAsync(
            version,
            [write],
            new SettingAuthor(null, "Test"),
            SettingChangeKind.Changed,
            null,
            default))!.Value;
    }

    private static IOptionsMonitor<RetentionOptions> Monitor(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddOptions<RetentionOptions>().Bind(configuration.GetSection("Veyra:Storage"));

        return services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<RetentionOptions>>();
    }

    /// <summary>A stand-in for a module's options, bound from Veyra:Storage like the real ones.</summary>
    public sealed class RetentionOptions
    {
        /// <summary>How many days of logs are kept.</summary>
        public int LogRetentionDays { get; set; } = 14;
    }
}
