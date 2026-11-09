using Microsoft.Extensions.Diagnostics.HealthChecks;
using Veyra.Testing;

namespace Veyra.Storage.Tests;

public class StorageHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_NotConfigured_ReturnsUnhealthy()
    {
        var check = new StorageHealthCheck(dataSource: null);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("not configured", result.Description, StringComparison.Ordinal);
    }
}

[Collection(PostgresGroup.Name)]
public class StorageHealthCheckDatabaseTests(PostgresFixture postgres)
{
    [Fact]
    public async Task CheckHealthAsync_DatabaseReachable_ReturnsHealthy()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var check = new StorageHealthCheck(database);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
