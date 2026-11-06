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
}
