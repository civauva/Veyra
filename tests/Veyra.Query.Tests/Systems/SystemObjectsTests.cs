using Veyra.Query.Systems;

namespace Veyra.Query.Tests.Systems;

/// <summary>
/// What counts as a database server's own housekeeping, which its screen hides unless asked (VEY-64). The statements
/// are from a real SQL Server: SSMS browsing the catalog, change tracking's cleanup, and Query Store's checks.
/// </summary>
public class SystemObjectsTests
{
    [Theory]
    [InlineData("microsoft.sql_server", "master", true)]
    [InlineData("microsoft.sql_server", "MSDB", true)]
    [InlineData("microsoft.sql_server", "model", true)]
    [InlineData("microsoft.sql_server", "tempdb", true)]
    [InlineData("microsoft.sql_server", "rdsadmin", true)]
    [InlineData("microsoft.sql_server", "Prosperus", false)]
    [InlineData("microsoft.sql_server", "postgres", false)]
    [InlineData("postgresql", "postgres", true)]
    [InlineData("postgresql", "template0", true)]
    [InlineData("postgresql", "template1", true)]
    [InlineData("postgresql", "azure_maintenance", true)]
    [InlineData("postgresql", "shop", false)]
    [InlineData("postgresql", "master", false)]
    [InlineData("microsoft.sql_server", null, false)]
    public void IsSystemDatabase_KnownNames_AreTheEnginesOwn(string engine, string? name, bool expected)
    {
        Assert.Equal(expected, SystemObjects.IsSystemDatabase(engine, name));
    }

    [Theory]
    [InlineData(
        "delete top(@batch_size) from sys.[change_tracking_2089058478] where sys_change_xdes_id in (select xdes_id "
        + "from sys.syscommittab ssct where ssct.commit_ts <= @csn_watermark)")]
    [InlineData("IF EXISTS (SELECT TOP(1) 1 FROM sys.query_store_runtime_stats)")]
    [InlineData(
        "SELECT SCHEMA_NAME(udf.schema_id) AS [Schema], udf.name AS [Name] FROM sys.all_objects AS udf "
        + "INNER JOIN sys.schemas AS s ON s.schema_id = udf.schema_id WHERE udf.type in ('TF', 'FN', 'IF')")]
    [InlineData("SELECT s.name AS [Name] FROM sys.schemas AS s ORDER BY [Name] ASC")]
    [InlineData("select host_platform from sys.dm_os_host_info")]
    [InlineData("SELECT dtb.name AS [DatabaseName] FROM master.sys.databases AS dtb WHERE (dtb.name=@_msparam_0)")]
    [InlineData("DELETE FROM MSchange_tracking_history WHERE start_time < @time")]
    [InlineData("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'")]
    [InlineData(
        "SELECT @SmoAuditLevel AS [AuditLevel], ISNULL(@NumErrorLogs, -1) AS [NumberOfLogFiles], (case when "
        + "@SmoLoginMode < 3 then @SmoLoginMode else 9 end) AS [LoginMode]")]
    [InlineData("SELECT CAST( serverproperty(N'Servername') AS sysname) AS [Server_Name]")]
    [InlineData("SELECT @@VERSION")]
    [InlineData("insert into #tmp_db_ars select replica_id, group_id from master.sys.availability_replicas")]
    [InlineData("SELECT j.name FROM msdb.dbo.sysjobs AS j CROSS APPLY sys.dm_exec_sql_text(j.job_id) AS t")]
    [InlineData("EXEC sp_server_diagnostics 5")]
    [InlineData("exec sys.sp_spaceused")]
    [InlineData(
        "WITH found AS (SELECT object_id FROM sys.objects WHERE type = 'U') SELECT COUNT(*) FROM found")]
    public void IsSystemStatement_HousekeepingInAUserDatabase_IsSystem(string text)
    {
        Assert.True(SystemObjects.IsSystemStatement("Prosperus", text));
    }

    [Theory]
    [InlineData("SELECT TOP (1000) [id] ,[balance] FROM [VeyraWorkload].[dbo].[accounts]")]
    [InlineData("UPDATE dbo.Orders SET Status = @status WHERE CustomerId = @customerId")]
    [InlineData("SELECT o.Id FROM dbo.Orders AS o INNER JOIN sys.objects AS so ON so.name = o.Name")]
    [InlineData("insert into #work select * from sales.Invoices")]
    [InlineData("SELECT * FROM #work")]
    [InlineData("EXEC dbo.RefreshTokens @batch = 100")]
    [InlineData("SELECT 1")]
    [InlineData("WITH recent AS (SELECT Id FROM dbo.Orders) SELECT COUNT(*) FROM recent")]
    [InlineData("MERGE INTO dbo.Stock AS t USING @rows AS s ON t.Id = s.Id WHEN MATCHED THEN UPDATE SET t.Qty = s.Qty")]
    [InlineData("SELECT value FROM OPENJSON(@json)")]
    [InlineData("")]
    public void IsSystemStatement_ApplicationWork_IsNotSystem(string text)
    {
        Assert.False(SystemObjects.IsSystemStatement("Prosperus", text));
    }

    [Fact]
    public void IsSystemStatement_AnyStatementInASystemDatabase_IsSystem()
    {
        Assert.True(SystemObjects.IsSystemStatement("master", "SELECT * FROM dbo.spt_values"));
        Assert.True(SystemObjects.IsSystemStatement("msdb", "UPDATE dbo.backupset SET name = @n"));
    }

    [Theory]
    [InlineData("syspolicy_purge_history", true)]
    [InlineData("SSIS Server Maintenance Job", true)]
    [InlineData("sysutility_get_views_data_into_cache_tables", true)]
    [InlineData("collection_set_1_noncached_collect_and_upload", true)]
    [InlineData("Agent history clean up: distribution", true)]
    [InlineData("Nightly full backup - reporting", false)]
    [InlineData("Purge sessions", false)]
    public void IsSystemJob_JobsSqlServerSetsUpItself_AreSystem(string name, bool expected)
    {
        Assert.Equal(expected, SystemObjects.IsSystemJob(name));
    }

    [Theory]
    [InlineData("XE_TIMER_EVENT", true)]
    [InlineData("PWAIT_EXTENSIBILITY_CLEANUP_TASK", true)]
    [InlineData("SOS_WORK_DISPATCHER", true)]
    [InlineData("sleep_task", true)]
    [InlineData("LCK_M_U", false)]
    [InlineData("WRITELOG", false)]
    [InlineData("PAGEIOLATCH_SH", false)]
    [InlineData(null, false)]
    public void IsIdleWait_BackgroundWaits_AreIdle(string? type, bool expected)
    {
        Assert.Equal(expected, SystemObjects.IsIdleWait(type));
    }
}
