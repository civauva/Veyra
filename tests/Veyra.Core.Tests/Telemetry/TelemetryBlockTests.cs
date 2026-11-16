using Veyra.Core.Alerts;
using Veyra.Core.Telemetry;

namespace Veyra.Core.Tests.Telemetry;

public class TelemetryBlockTests
{
    [Fact]
    public void Of_ServiceName_IsTheService()
    {
        var block = TelemetryBlock.Of(Resource(
            ("service.name", "checkout"),
            ("host.name", "web-01"),
            ("container.name", "checkout-1")));

        Assert.Equal(new TelemetryBlock(AlertBlockKind.Service, "checkout"), block);
    }

    [Fact]
    public void Of_DatabaseSystemAndServer_IsTheDatabaseServer()
    {
        var block = TelemetryBlock.Of(Resource(
            ("db.system.name", "microsoft.sql_server"),
            ("server.address", "sql-prod-01"),
            ("host.name", "db-host-01")));

        Assert.Equal(new TelemetryBlock(AlertBlockKind.Database, "microsoft.sql_server sql-prod-01"), block);
    }

    [Fact]
    public void Of_SqlServerWithoutServerAddress_UsesItsInstanceId()
    {
        var block = TelemetryBlock.Of(Resource(
            ("db.system.name", "microsoft.sql_server"),
            ("service.instance.id", "mssql:1433")));

        Assert.Equal(new TelemetryBlock(AlertBlockKind.Database, "microsoft.sql_server mssql:1433"), block);
    }

    [Fact]
    public void Of_DatabaseSystemWithoutServer_IsNotADatabase()
    {
        var block = TelemetryBlock.Of(Resource(("db.system.name", "postgresql"), ("host.name", "db-01")));

        Assert.Equal(new TelemetryBlock(AlertBlockKind.Host, "db-01"), block);
    }

    [Fact]
    public void Of_ContainerName_IsTheContainer()
    {
        var block = TelemetryBlock.Of(Resource(("container.name", "nginx"), ("host.name", "web-01")));

        Assert.Equal(new TelemetryBlock(AlertBlockKind.Container, "nginx"), block);
    }

    [Fact]
    public void Of_HostNameOnly_IsTheHost()
    {
        var block = TelemetryBlock.Of(Resource(("host.name", "web-01")));

        Assert.Equal(new TelemetryBlock(AlertBlockKind.Host, "web-01"), block);
    }

    [Fact]
    public void Of_NothingKnown_IsTheUnknownService()
    {
        var block = TelemetryBlock.Of(Resource(("host.name", ""), ("telemetry.sdk.name", "opentelemetry")));

        Assert.Equal(new TelemetryBlock(AlertBlockKind.Service, "unknown_service"), block);
    }

    [Fact]
    public void Database_Name_SplitsIntoEngineAndServer()
    {
        var block = new TelemetryBlock(AlertBlockKind.Database, "microsoft.sql_server sql-prod-01");

        Assert.Equal("microsoft.sql_server", block.Engine);
        Assert.Equal("sql-prod-01", block.Server);
        Assert.Equal(block, TelemetryBlock.Database("microsoft.sql_server", "sql-prod-01"));
    }

    private static Dictionary<string, string> Resource(params (string Key, string Value)[] attributes) =>
        attributes.ToDictionary(attribute => attribute.Key, attribute => attribute.Value);
}
