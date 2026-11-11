using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Veyra.Core.Telemetry;

namespace Veyra.Storage.Tests;

public class StorageServiceCollectionExtensionsTests
{
    [Fact]
    public void AddVeyraStorage_BindsConnectionStringFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Veyra:Storage:ConnectionString"] = "Host=localhost;Database=veyra",
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddVeyraStorage(configuration)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<StorageOptions>>().Value;

        Assert.Equal("Host=localhost;Database=veyra", options.ConnectionString);
    }

    [Fact]
    public void AddVeyraStorage_EmptyStorageBudget_MeansNoBudget()
    {
        // Compose passes an empty value when VEYRA_MAX_LOG_STORAGE_GB is not set.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Veyra:Storage:LogRetentionDays"] = "30",
                ["Veyra:Storage:MaxLogStorageGigabytes"] = "",
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddVeyraStorage(configuration)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<StorageOptions>>().Value;

        Assert.Equal(30, options.LogRetentionDays);
        Assert.Null(options.MaxLogStorageGigabytes);
    }

    [Fact]
    public async Task AddVeyraStorage_WithoutConnectionString_RatesAndUsageFailWithWhatToSet()
    {
        using var provider = new ServiceCollection()
            .AddVeyraStorage(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

        var rates = provider.GetRequiredService<ILogRates>();
        var usage = provider.GetRequiredService<ILogStorageUsage>();

        var ratesError = await Assert.ThrowsAsync<InvalidOperationException>(
            () => rates.GetServiceRatesAsync(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow, default));
        var usageError = await Assert.ThrowsAsync<InvalidOperationException>(() => usage.GetUsageAsync(default));
        Assert.Contains("Veyra__Storage__ConnectionString", ratesError.Message, StringComparison.Ordinal);
        Assert.Contains("Veyra__Storage__ConnectionString", usageError.Message, StringComparison.Ordinal);
    }
}
