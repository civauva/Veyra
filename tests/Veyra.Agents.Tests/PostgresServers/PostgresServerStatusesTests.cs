using Veyra.Agents.Agents;
using Veyra.Agents.PostgresServers;
using Veyra.Agents.SqlServers;

namespace Veyra.Agents.Tests.PostgresServers;

public class PostgresServerStatusesTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly PostgresServer _server = new(
        Id: Guid.CreateVersion7(),
        Name: "pg-main",
        Address: "10.0.0.5",
        Port: 5432,
        Environment: "production",
        AgentId: Guid.CreateVersion7(),
        Login: "veyra_monitor",
        Encryption: PostgresEncryption.Required,
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
        var status = PostgresServerStatuses.Of(_server, AgentReport.None, [], lastData: null, _now);

        Assert.Equal(SqlServerState.AgentNotConnected, status.State);
    }

    // The errors of lib/pq, which both the postgresql and the sqlquery receivers use, and of Go's network code.
    [Theory]
    [InlineData(
        "pq: password authentication failed for user \"veyra_monitor\"",
        SqlServerState.LoginFailed)]
    [InlineData("pq: role \"veyra_monitor\" does not exist", SqlServerState.LoginFailed)]
    [InlineData(
        "pq: no pg_hba.conf entry for host \"10.0.0.9\", user \"veyra_monitor\", database \"postgres\", no encryption",
        SqlServerState.LoginFailed)]
    [InlineData("dial tcp 10.0.0.5:5432: connect: connection refused", SqlServerState.Unreachable)]
    [InlineData("dial tcp: lookup pg-main on 127.0.0.11:53: no such host", SqlServerState.Unreachable)]
    [InlineData("dial tcp 10.0.0.5:5432: i/o timeout", SqlServerState.Unreachable)]
    [InlineData(
        "tls: failed to verify certificate: x509: certificate signed by unknown authority",
        SqlServerState.CertificateRejected)]
    [InlineData("pq: SSL is not enabled on the server", SqlServerState.CertificateRejected)]
    [InlineData("pq: permission denied for function pg_ls_waldir", SqlServerState.GrantMissing)]
    [InlineData("something unexpected", SqlServerState.Failing)]
    public void Of_RecentReaderError_SaysWhatItMeans(string error, SqlServerState expected)
    {
        var errors = new[] { new ReaderError(_server.Id, error, _now.AddSeconds(-10)) };

        var status = PostgresServerStatuses.Of(_server, _online, errors, lastData: _now.AddSeconds(-30), _now);

        Assert.Equal(new SqlServerStatus(expected, error, _now.AddSeconds(-10)), status);
    }

    [Fact]
    public void Of_DataAfterAFailedLogin_IsReceiving()
    {
        var errors = new[]
        {
            new ReaderError(_server.Id, "pq: password authentication failed for user \"x\"", _now.AddSeconds(-90)),
        };

        var status = PostgresServerStatuses.Of(_server, _online, errors, lastData: _now.AddSeconds(-10), _now);

        Assert.Equal(new SqlServerStatus(SqlServerState.Receiving, null, _now.AddSeconds(-10)), status);
    }

    [Fact]
    public void Of_AnotherServersError_IsIgnored()
    {
        var errors = new[] { new ReaderError(Guid.CreateVersion7(), "pq: password authentication failed", _now) };

        var status = PostgresServerStatuses.Of(_server, _online, errors, lastData: null, _now);

        Assert.Equal(new SqlServerStatus(SqlServerState.WaitingForData, null, null), status);
    }

    [Fact]
    public void Of_ErrorFromBeforeTheLastChange_IsIgnored()
    {
        var changed = _server with { ChangedAt = _now.AddSeconds(-5) };
        var errors = new[] { new ReaderError(changed.Id, "pq: password authentication failed", _now.AddSeconds(-10)) };

        var status = PostgresServerStatuses.Of(changed, _online, errors, lastData: null, _now);

        Assert.Equal(SqlServerState.WaitingForData, status.State);
    }

    [Fact]
    public void Of_DataArriving_IsReceiving()
    {
        var status = PostgresServerStatuses.Of(_server, _online, [], lastData: _now.AddSeconds(-40), _now);

        Assert.Equal(new SqlServerStatus(SqlServerState.Receiving, null, _now.AddSeconds(-40)), status);
    }
}
