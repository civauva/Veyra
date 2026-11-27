using Veyra.Core.Alerts;

namespace Veyra.Core.Tests.Alerts;

public sealed class BlockLinksTests
{
    private static readonly BlockLink[] _links =
    [
        new(AlertBlockKind.Service, "checkout", "production", "web-01"),
        new(AlertBlockKind.Database, "microsoft.sql_server sql-prod-01", "production", "db-01"),
        new(AlertBlockKind.Service, "billing", null, "web-02"),
    ];

    [Fact]
    public void IsRelated_SameBlockOtherPart_IsRelated()
    {
        Assert.True(BlockLinks.IsRelated(
            AlertSubject.Host("web-01", "/"),
            AlertSubject.Host("web-01", "/var"),
            "production",
            _links));
    }

    [Fact]
    public void IsRelated_SameContainerNameOnAnotherHost_IsNotRelated()
    {
        Assert.False(BlockLinks.IsRelated(
            AlertSubject.Container("shop-api", "web-01"),
            AlertSubject.Container("shop-api", "web-02"),
            "production",
            _links));
    }

    [Fact]
    public void IsRelated_ContainerAndItsHost_IsRelatedEitherWay()
    {
        var container = AlertSubject.Container("shop-api", "web-01");
        var host = AlertSubject.Host("WEB-01", "/");

        Assert.True(BlockLinks.IsRelated(container, host, "production", []));
        Assert.True(BlockLinks.IsRelated(host, container, "production", []));
    }

    [Fact]
    public void IsRelated_ServiceAndAHostItRunsOn_IsRelatedEitherWay()
    {
        var service = AlertSubject.Service("checkout");
        var host = AlertSubject.Host("web-01");

        Assert.True(BlockLinks.IsRelated(service, host, "production", _links));
        Assert.True(BlockLinks.IsRelated(host, service, "production", _links));
    }

    [Fact]
    public void IsRelated_DatabaseServerAndItsHost_IsRelated()
    {
        Assert.True(BlockLinks.IsRelated(
            AlertSubject.Database("microsoft.sql_server sql-prod-01", "shop"),
            AlertSubject.Host("db-01", "/"),
            "production",
            _links));
    }

    [Fact]
    public void IsRelated_ServiceAndAHostItDoesNotRunOn_IsNotRelated()
    {
        Assert.False(BlockLinks.IsRelated(
            AlertSubject.Service("checkout"),
            AlertSubject.Host("web-02"),
            "production",
            _links));
    }

    [Fact]
    public void IsRelated_TwoServicesOnOneHost_IsNotRelated()
    {
        Assert.False(BlockLinks.IsRelated(
            AlertSubject.Service("checkout"),
            AlertSubject.Service("billing"),
            "production",
            [.. _links, new BlockLink(AlertBlockKind.Service, "billing", "production", "web-01")]));
    }

    [Fact]
    public void IsRelated_LinkOfAnotherEnvironment_DoesNotCount()
    {
        Assert.False(BlockLinks.IsRelated(
            AlertSubject.Service("checkout"),
            AlertSubject.Host("web-01"),
            "staging",
            _links));
    }

    [Fact]
    public void IsRelated_LinkWithoutEnvironment_CountsForEvery()
    {
        Assert.True(BlockLinks.IsRelated(
            AlertSubject.Service("billing"),
            AlertSubject.Host("web-02"),
            "staging",
            _links));
    }

    [Fact]
    public void HostsOf_Container_IsItsPart()
    {
        Assert.Equal(["web-01"], BlockLinks.HostsOf(AlertSubject.Container("shop-api", "web-01"), null, []));
    }

    [Fact]
    public void HostsOf_Service_AreItsLinks()
    {
        Assert.Equal(["web-01"], BlockLinks.HostsOf(AlertSubject.Service("checkout"), "production", _links));
    }

    [Fact]
    public void RunningOn_Host_ListsServicesAndDatabaseServersOnIt()
    {
        var running = BlockLinks.RunningOn("db-01", "production", _links);

        Assert.Equal([AlertSubject.Database("microsoft.sql_server sql-prod-01")], running);
    }
}
