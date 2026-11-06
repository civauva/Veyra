using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Veyra.Alerting.Tests;

public class AlertingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddVeyraAlerting_BindsEvaluationIntervalFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Veyra:Alerting:EvaluationInterval"] = "00:01:00" })
            .Build();

        using var provider = new ServiceCollection()
            .AddVeyraAlerting(configuration)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AlertingOptions>>().Value;

        Assert.Equal(TimeSpan.FromMinutes(1), options.EvaluationInterval);
    }
}
