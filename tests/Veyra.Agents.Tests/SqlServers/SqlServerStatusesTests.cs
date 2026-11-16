using Veyra.Agents.Agents;
using Veyra.Agents.SqlServers;

namespace Veyra.Agents.Tests.SqlServers;

public class SqlServerStatusesTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly SqlServer _server = new(
        Id: Guid.CreateVersion7(),
        Name: "sql-prod-01",
        Address: "10.0.0.5",
        Port: 1433,
        Environment: "production",
        AgentId: Guid.CreateVersion7(),
        Login: "veyra_monitor",
        Encryption: SqlServerEncryption.Required,
        CreatedAt: _now.AddHours(-1),
        ChangedAt: _now.AddHours(-1));

    private static readonly AgentReport _online = AgentReport.None with
    {
        LastSeenAt = _now.AddSeconds(-20),
        Healthy = true,
        ConfigState = AgentConfigState.Applied,
    };

    [Fact]
    public void Of_AgentNeverConnected_SaysSo()
    {
        var status = SqlServerStatuses.Of(_server, AgentReport.None, [], lastData: null, _now);

        Assert.Equal(SqlServerState.AgentNotConnected, status.State);
    }

    [Fact]
    public void Of_AgentSilentForOverTwoMinutes_IsOffline()
    {
        var report = _online with { LastSeenAt = _now.AddMinutes(-3) };

        var status = SqlServerStatuses.Of(_server, report, [], lastData: _now.AddMinutes(-3), _now);

        Assert.Equal(SqlServerState.AgentOffline, status.State);
        Assert.Equal(report.LastSeenAt, status.Since);
    }

    [Fact]
    public void Of_ConfigurationRefused_SaysWhy()
    {
        var report = _online with { ConfigState = AgentConfigState.Failed, ConfigError = "unknown type" };

        var status = SqlServerStatuses.Of(_server, report, [], lastData: null, _now);

        Assert.Equal(new SqlServerStatus(SqlServerState.ConfigurationFailed, "unknown type", null), status);
    }

    [Theory]
    [InlineData(
        "sqlServerScraperHelper: mssql: login error: Login failed for user 'veyra_monitor'.",
        SqlServerState.LoginFailed)]
    [InlineData(
        "unable to open tcp connection with host '10.0.0.5:1433': dial tcp 10.0.0.5:1433: connect: connection refused",
        SqlServerState.Unreachable)]
    [InlineData("dial tcp: lookup sql-prod-01: no such host", SqlServerState.Unreachable)]
    [InlineData("dial tcp 10.0.0.5:1433: i/o timeout", SqlServerState.Unreachable)]
    [InlineData(
        "TLS Handshake failed: tls: failed to verify certificate: x509: certificate signed by unknown authority",
        SqlServerState.CertificateRejected)]
    [InlineData(
        "mssql: The SELECT permission was denied on the object 'backupset', database 'msdb', schema 'dbo'.",
        SqlServerState.GrantMissing)]
    [InlineData(
        "mssql: VIEW SERVER STATE permission was denied on object 'server', database 'master'.",
        SqlServerState.GrantMissing)]
    [InlineData("something unexpected", SqlServerState.Failing)]
    public void Of_RecentReaderError_SaysWhatItMeans(string error, SqlServerState expected)
    {
        var errors = new[] { new ReaderError(_server.Id, error, _now.AddSeconds(-10)) };

        var status = SqlServerStatuses.Of(_server, _online, errors, lastData: _now.AddSeconds(-30), _now);

        Assert.Equal(new SqlServerStatus(expected, error, _now.AddSeconds(-10)), status);
    }

    [Fact]
    public void Of_DataAfterAFailedLogin_IsReceiving()
    {
        // The login script was run after the agent last failed: the figures that follow say it is fixed.
        var errors = new[] { new ReaderError(_server.Id, "Login failed for user 'x'.", _now.AddSeconds(-90)) };

        var status = SqlServerStatuses.Of(_server, _online, errors, lastData: _now.AddSeconds(-10), _now);

        Assert.Equal(new SqlServerStatus(SqlServerState.Receiving, null, _now.AddSeconds(-10)), status);
    }

    [Fact]
    public void Of_DataAlongsideAMissingGrant_StillSaysTheGrantIsMissing()
    {
        // A missing grant stops one query; the others go on sending figures.
        var errors = new[]
        {
            new ReaderError(
                _server.Id,
                "The SELECT permission was denied on the object 'backupset'.",
                _now.AddSeconds(-50)),
        };

        var status = SqlServerStatuses.Of(_server, _online, errors, lastData: _now.AddSeconds(-5), _now);

        Assert.Equal(SqlServerState.GrantMissing, status.State);
    }

    [Fact]
    public void Of_ErrorFromBeforeTheLastChange_IsIgnored()
    {
        var changed = _server with { ChangedAt = _now.AddSeconds(-5) };
        var errors = new[] { new ReaderError(changed.Id, "Login failed for user 'x'.", _now.AddSeconds(-10)) };

        var status = SqlServerStatuses.Of(changed, _online, errors, lastData: null, _now);

        Assert.Equal(SqlServerState.WaitingForData, status.State);
    }

    [Fact]
    public void Of_OldError_IsIgnored()
    {
        var errors = new[] { new ReaderError(_server.Id, "Login failed for user 'x'.", _now.AddMinutes(-3)) };

        var status = SqlServerStatuses.Of(_server, _online, errors, lastData: _now.AddSeconds(-30), _now);

        Assert.Equal(SqlServerState.Receiving, status.State);
    }

    [Fact]
    public void Of_AnotherServersError_IsIgnored()
    {
        var errors = new[] { new ReaderError(Guid.CreateVersion7(), "Login failed for user 'x'.", _now) };

        var status = SqlServerStatuses.Of(_server, _online, errors, lastData: null, _now);

        Assert.Equal(SqlServerState.WaitingForData, status.State);
    }

    [Fact]
    public void Of_DataArriving_IsReceiving()
    {
        var status = SqlServerStatuses.Of(_server, _online, [], lastData: _now.AddSeconds(-40), _now);

        Assert.Equal(new SqlServerStatus(SqlServerState.Receiving, null, _now.AddSeconds(-40)), status);
    }

    [Fact]
    public void Of_NoErrorNoData_IsWaiting()
    {
        var status = SqlServerStatuses.Of(_server, _online, [], lastData: null, _now);

        Assert.Equal(new SqlServerStatus(SqlServerState.WaitingForData, null, null), status);
    }

    [Theory]
    [InlineData("sqlserver/0195a000000070008000000000000001", true)]
    [InlineData("sqlquery/0195a000000070008000000000000001", true)]
    [InlineData("sqlquery/live/0195a000000070008000000000000001", true)]
    [InlineData("otlphttp", false)]
    [InlineData("sqlserver/not-an-id", false)]
    public void ServerOf_ComponentId_FindsTheServer(string componentId, bool found)
    {
        var id = SqlServerStatuses.ServerOf(componentId);

        Assert.Equal(found ? new Guid("0195a000-0000-7000-8000-000000000001") : null, id);
    }
}
