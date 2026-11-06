using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Veyra.Query.Tests;

public class QueryServiceCollectionExtensionsTests
{
    [Fact]
    public void AddVeyraQuery_UsesDefaultsWhenSectionMissing()
    {
        var configuration = new ConfigurationBuilder().Build();

        using var provider = new ServiceCollection()
            .AddVeyraQuery(configuration)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<QueryOptions>>().Value;

        Assert.Equal(1_000, options.MaxPageSize);
    }
}
