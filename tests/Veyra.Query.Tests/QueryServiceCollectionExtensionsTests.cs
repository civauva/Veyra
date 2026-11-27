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
        Assert.Equal(100_000, options.MaxExportRecords);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(10_000_000, true)]
    [InlineData(10_000_001, false)]
    public void Validate_MaxExportRecords_IsBetweenOneAndTenMillion(int records, bool valid)
    {
        var result = new QueryOptionsRules().Validate(null, new QueryOptions { MaxExportRecords = records });

        Assert.Equal(valid, result.Succeeded);
    }
}
