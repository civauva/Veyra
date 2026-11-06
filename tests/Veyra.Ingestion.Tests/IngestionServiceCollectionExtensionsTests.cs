using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Veyra.Ingestion.Tests;

public class IngestionServiceCollectionExtensionsTests
{
    [Fact]
    public void AddVeyraIngestion_BindsOptionsFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Veyra:Ingestion:MaxQueueLength"] = "42" })
            .Build();

        using var provider = new ServiceCollection()
            .AddVeyraIngestion(configuration)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<IngestionOptions>>().Value;

        Assert.Equal(42, options.MaxQueueLength);
    }

    [Fact]
    public void AddVeyraIngestion_InvalidQueueLength_FailsValidation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Veyra:Ingestion:MaxQueueLength"] = "0" })
            .Build();

        using var provider = new ServiceCollection()
            .AddVeyraIngestion(configuration)
            .BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<IngestionOptions>>().Value);
    }
}
