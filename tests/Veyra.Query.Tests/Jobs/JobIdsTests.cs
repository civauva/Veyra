using Veyra.Core.Alerts;
using Veyra.Core.Jobs;
using Veyra.Core.Telemetry;
using Veyra.Query.Jobs;

namespace Veyra.Query.Tests.Jobs;

/// <summary>A job's ID in the API (ADR 0027): opaque, safe in a URL, and the same every time.</summary>
public class JobIdsTests
{
    [Theory]
    [InlineData("production")]
    [InlineData(null)]
    public void Encode_ThenTryDecode_GivesTheSameJob(string? environment)
    {
        var job = new JobKey(
            JobSource.SqlAgent,
            environment,
            TelemetryBlock.Database("microsoft.sql_server", "SQL-PROD-01"),
            "Nightly backup | full/diff?");

        var id = JobIds.Encode(job);

        Assert.Matches("^[A-Za-z0-9_-]+$", id);
        Assert.Equal(id, JobIds.Encode(job));
        Assert.True(JobIds.TryDecode(id, out var decoded));
        Assert.Equal(job, decoded);
    }

    [Fact]
    public void TryDecode_TracedJob_IsOnItsService()
    {
        var billing = new TelemetryBlock(AlertBlockKind.Service, "billing");
        var job = new JobKey(JobSource.Trace, "staging", billing, "import");

        Assert.True(JobIds.TryDecode(JobIds.Encode(job), out var decoded));
        Assert.Equal(job, decoded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 at all!")]
    [InlineData("WyJ0cmFjZSJd")]
    [InlineData("WyJjcm9uIixudWxsLCJiIiwiaiJd")]
    [InlineData("e30")]
    public void TryDecode_SomethingElse_IsNoJob(string id)
    {
        Assert.False(JobIds.TryDecode(id, out _));
    }
}
