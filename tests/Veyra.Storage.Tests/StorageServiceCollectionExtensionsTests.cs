using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
}
