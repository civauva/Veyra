using Veyra.Query.Server;

namespace Veyra.Query.Tests.Server;

public class PermissionsViewTests
{
    [Fact]
    public void Build_NothingReported_SaysSoAndBlamesNoTab()
    {
        var view = PermissionsViewBuilder.Build(new Dictionary<string, double>(), null);

        Assert.False(view.Reported);
        Assert.Empty(view.Gaps);
        Assert.Null(view.Script);
    }

    [Fact]
    public void Build_EveryPermissionGranted_LeavesNoGapAndNoScript()
    {
        var view = PermissionsViewBuilder.Build(
            new Dictionary<string, double> { ["VIEW SERVER STATE"] = 0, ["SELECT ON msdb.dbo.sysjobsteps"] = 0 },
            "veyra_monitor");

        Assert.True(view.Reported);
        Assert.Empty(view.Gaps);
        Assert.Null(view.Script);
    }

    [Fact]
    public void Build_MissingGrants_NamesTheTabsTheyBlindAndTheScriptThatGrantsThem()
    {
        var view = PermissionsViewBuilder.Build(
            new Dictionary<string, double>
            {
                ["VIEW SERVER STATE"] = 0,
                ["CONNECT ANY DATABASE"] = 1,
                ["SELECT ON msdb.dbo.sysjobsteps"] = 1,
                ["EXECUTE ON msdb.dbo.agent_datetime"] = 1,
            },
            "veyra_monitor");

        Assert.Equal(
            ["CONNECT ANY DATABASE", "EXECUTE ON msdb.dbo.agent_datetime", "SELECT ON msdb.dbo.sysjobsteps"],
            view.Gaps.Select(gap => gap.Permission));
        Assert.Equal(["queries", "databases"], view.Gaps[0].Tabs);
        Assert.Equal(["jobs"], view.Gaps[2].Tabs);
        Assert.Equal(
            """
            USE master;
            GRANT CONNECT ANY DATABASE TO [veyra_monitor];
            GO
            USE msdb;
            IF USER_ID(N'veyra_monitor') IS NULL CREATE USER [veyra_monitor] FOR LOGIN [veyra_monitor];
            GRANT EXECUTE ON dbo.agent_datetime TO [veyra_monitor];
            GRANT SELECT ON dbo.sysjobsteps TO [veyra_monitor];
            GO
            """.ReplaceLineEndings("\n"),
            view.Script);
    }

    [Fact]
    public void Build_LoginWithQuotes_QuotesItInTheScript()
    {
        var view = PermissionsViewBuilder.Build(
            new Dictionary<string, double> { ["SELECT ON msdb.dbo.backupset"] = 1 },
            "odd]na'me");

        Assert.Contains(
            "IF USER_ID(N'odd]na''me') IS NULL CREATE USER [odd]]na'me] FOR LOGIN [odd]]na'me];",
            view.Script);
    }

    [Fact]
    public void Build_UnknownPermission_IsLeftOut()
    {
        var view = PermissionsViewBuilder.Build(new Dictionary<string, double> { ["ALTER ANY LOGIN"] = 1 }, "veyra");

        Assert.True(view.Reported);
        Assert.Empty(view.Gaps);
    }

    [Fact]
    public void Build_AzureSqlDatabase_GrantsInsideTheDatabaseWithoutSwitching()
    {
        var view = PermissionsViewBuilder.Build(
            new Dictionary<string, double> { ["VIEW DATABASE STATE"] = 1, ["VIEW DEFINITION"] = 0 },
            "veyra_monitor",
            "azure-sql-database");

        Assert.Equal("azure-sql-database", view.Platform);
        Assert.Equal(["live", "queries", "blocking", "databases"], view.Gaps.Single().Tabs);
        Assert.Equal("GRANT VIEW DATABASE STATE TO [veyra_monitor];", view.Script);
    }

    [Fact]
    public void Build_AgentTooOldToSayItsPlatform_ReadsASqlServer()
    {
        var view = PermissionsViewBuilder.Build(new Dictionary<string, double> { ["VIEW SERVER STATE"] = 0 }, "veyra");

        Assert.Equal("sqlserver", view.Platform);
    }
}
