using Veyra.Agents.Collector;
using Veyra.Agents.SqlServers;
using YamlDotNet.RepresentationModel;

namespace Veyra.Agents.Tests.Collector;

public class CollectorConfigTests
{
    private static readonly AgentConnection _connection = new(new Uri("http://veyra:8080"), "vyk_agentkey");

    private static readonly SqlServer _prod = Server(
        new Guid("0195a000-0000-7000-8000-000000000001"),
        "sql-prod-01",
        "10.0.0.5",
        SqlServerEncryption.Required);

    private static readonly SqlServer _test = Server(
        new Guid("0195a000-0000-7000-8000-000000000002"),
        "sql-test-01",
        "sqltest.internal",
        SqlServerEncryption.TrustServerCertificate);

    [Fact]
    public void Build_OneServer_FillsInEveryValue()
    {
        var text = CollectorConfig.Build(_connection, [new SqlServerTarget(_prod, "s3cret")]);

        Assert.DoesNotContain("${env:", text, StringComparison.Ordinal);

        var root = Parse(text);
        var suffix = CollectorConfig.ComponentSuffix(_prod.Id);
        var datasource = Scalar(root, "receivers", $"sqlserver/{suffix}", "datasource");
        Assert.Equal(
            "sqlserver://veyra_monitor:s3cret@10.0.0.5:1433?encrypt=true&TrustServerCertificate=false" +
            "&app+name=veyra-collector&database=master",
            datasource);
        Assert.Equal(datasource, Scalar(root, "receivers", $"sqlquery/{suffix}", "datasource"));
        Assert.Equal("http://veyra:8080", Scalar(root, "exporters", "otlphttp", "endpoint"));
        Assert.Equal("Bearer vyk_agentkey", Scalar(root, "exporters", "otlphttp", "headers", "Authorization"));
    }

    [Fact]
    public void Build_OneServer_NamesTheServerAndItsEnvironment()
    {
        var root = Parse(CollectorConfig.Build(_connection, [new SqlServerTarget(_prod, "s3cret")]));

        var attributes = (YamlSequenceNode)Node(
            root,
            "processors",
            $"resource/{CollectorConfig.ComponentSuffix(_prod.Id)}",
            "attributes");
        var values = attributes.Children
            .Cast<YamlMappingNode>()
            .ToDictionary(attribute => Text(attribute, "key"), attribute => Text(attribute, "value"));

        Assert.Equal("microsoft.sql_server", values["db.system.name"]);
        Assert.Equal("sql-prod-01", values["server.address"]);
        Assert.Equal("production", values["deployment.environment.name"]);
    }

    [Fact]
    public void Build_TwoServers_GivesEachItsOwnReceiversAndPipelines()
    {
        var root = Parse(CollectorConfig.Build(
            _connection,
            [new SqlServerTarget(_prod, "a"), new SqlServerTarget(_test, "b")]));

        var prod = CollectorConfig.ComponentSuffix(_prod.Id);
        var test = CollectorConfig.ComponentSuffix(_test.Id);
        var pipelines = (YamlMappingNode)Node(root, "service", "pipelines");

        Assert.Equal(
            [
                $"metrics/{prod}",
                $"metrics/queries/{prod}",
                $"logs/live/{prod}",
                $"metrics/{test}",
                $"metrics/queries/{test}",
                $"logs/live/{test}",
            ],
            pipelines.Children.Keys.Select(key => ((YamlScalarNode)key).Value));
        Assert.Equal([$"sqlserver/{test}"], Sequence(root, "service", "pipelines", $"metrics/{test}", "receivers"));
        Assert.Equal(
            ["memory_limiter", "filter/connection-only", $"resource/{test}", "batch"],
            Sequence(root, "service", "pipelines", $"metrics/{test}", "processors"));
        Assert.Equal(
            [$"sqlquery/{test}", $"sqlquery/storage/{test}", $"sqlquery/server/{test}", $"sqlquery/jobs/{test}"],
            Sequence(root, "service", "pipelines", $"metrics/queries/{test}", "receivers"));
        Assert.Equal(
            ["memory_limiter", "cumulativetodelta/statements", $"resource/{test}", "batch"],
            Sequence(root, "service", "pipelines", $"metrics/queries/{test}", "processors"));
        Assert.Equal(
            [
                $"sqlquery/live/{test}",
                $"sqlquery/plans/{test}",
                $"sqlquery/events/{test}",
                $"sqlquery/querystore/{test}",
                $"sqlquery/indexes/{test}",
                $"sqlquery/jobs/{test}",
                $"sqlquery/config/{test}",
                $"sqlquery/backups/{test}",
            ],
            Sequence(root, "service", "pipelines", $"logs/live/{test}", "receivers"));
        Assert.Equal(
            ["memory_limiter", $"resource/{test}", "batch/live"],
            Sequence(root, "service", "pipelines", $"logs/live/{test}", "processors"));
        Assert.Equal(["otlphttp"], Sequence(root, "service", "pipelines", $"logs/live/{test}", "exporters"));
        Assert.Contains("@sqltest.internal:1433?", Scalar(root, "receivers", $"sqlquery/live/{test}", "datasource"));
        Assert.Contains("@sqltest.internal:1433?", Scalar(root, "receivers", $"sqlserver/{test}", "datasource"));
    }

    [Fact]
    public void Build_PasswordWithSpecialCharacters_IsEscapedSoNeitherTheUrlNorTheCollectorReadsIt()
    {
        var text = CollectorConfig.Build(_connection, [new SqlServerTarget(_prod, "p@ss:w$rd/1&x=${env:HOME}")]);

        var datasource = Scalar(
            Parse(text),
            "receivers",
            $"sqlserver/{CollectorConfig.ComponentSuffix(_prod.Id)}",
            "datasource");

        Assert.StartsWith("sqlserver://veyra_monitor:p%40ss%3Aw%24rd%2F1%26x%3D%24%7Benv%3AHOME%7D@", datasource);
        Assert.DoesNotContain("${env:", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SqlServerEncryption.Required, "encrypt=true&TrustServerCertificate=false")]
    [InlineData(SqlServerEncryption.TrustServerCertificate, "encrypt=true&TrustServerCertificate=true")]
    [InlineData(SqlServerEncryption.Off, "encrypt=false&TrustServerCertificate=false")]
    public void Build_Encryption_SetsTheConnectionFlags(SqlServerEncryption encryption, string flags)
    {
        var server = _prod with { Encryption = encryption };

        var datasource = Scalar(
            Parse(CollectorConfig.Build(_connection, [new SqlServerTarget(server, "x")])),
            "receivers",
            $"sqlserver/{CollectorConfig.ComponentSuffix(server.Id)}",
            "datasource");

        Assert.Contains($"?{flags}&", datasource, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NoServers_IsAnIdleConfigurationTheCollectorAccepts()
    {
        var root = Parse(CollectorConfig.Build(_connection, []));

        Assert.Equal(["nop"], Sequence(root, "service", "pipelines", "logs/idle", "receivers"));
        Assert.Equal(["nop"], Sequence(root, "service", "pipelines", "logs/idle", "exporters"));
        Assert.False(((YamlMappingNode)Node(root, "receivers")).Children.ContainsKey(new YamlScalarNode("sqlserver")));
    }

    [Fact]
    public void Build_SameInput_GivesTheSameText()
    {
        SqlServerTarget[] targets = [new(_prod, "a"), new(_test, "b")];

        Assert.Equal(CollectorConfig.Build(_connection, targets), CollectorConfig.Build(_connection, targets));
    }

    [Fact]
    public void ComponentSuffix_IsTheIdWithoutDashes()
    {
        Assert.Equal("0195a000000070008000000000000001", CollectorConfig.ComponentSuffix(_prod.Id));
    }

    private static SqlServer Server(
        Guid id,
        string name,
        string address,
        SqlServerEncryption encryption,
        SqlServerPlatform platform = SqlServerPlatform.SqlServer,
        string? database = null) =>
        new(
            Id: id,
            Name: name,
            Address: address,
            Port: 1433,
            Environment: "production",
            AgentId: Guid.Empty,
            Login: "veyra_monitor",
            Encryption: encryption,
            CreatedAt: DateTimeOffset.UnixEpoch,
            ChangedAt: DateTimeOffset.UnixEpoch,
            Platform: platform,
            Database: database);

    [Fact]
    public void Build_SqlServer_ReadsFromMasterAndSaysItIsASqlServer()
    {
        var text = CollectorConfig.Build(_connection, [new SqlServerTarget(_prod, "a")]);

        Assert.Contains("&database=master", text, StringComparison.Ordinal);
        Assert.Contains("'sqlserver' AS platform", text, StringComparison.Ordinal);
        Assert.DoesNotContain("veyra-agent: resource stats", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_SqlServer_ReadsTheBackupHistoryEveryFiveMinutesAsRecords()
    {
        var text = CollectorConfig.Build(_connection, [new SqlServerTarget(_prod, "a")]);
        var root = Parse(text);
        var suffix = CollectorConfig.ComponentSuffix(_prod.Id);

        Assert.Equal("5m", Scalar(root, "receivers", $"sqlquery/backups/{suffix}", "collection_interval"));
        Assert.Contains("veyra-agent: backups", text, StringComparison.Ordinal);
        Assert.Contains("'sqlserver.backup' AS veyra_record", text, StringComparison.Ordinal);
        Assert.Contains("msdb.dbo.backupmediafamily", text, StringComparison.Ordinal);
        Assert.Contains(
            $"sqlquery/backups/{suffix}",
            Sequence(root, "service", "pipelines", $"logs/live/{suffix}", "receivers"));
        AssertEveryPipelineReceiverExists(root);
    }

    [Fact]
    public void Build_SqlServer_ReadsTheMachinesMemoryAndOperatingSystem()
    {
        var text = CollectorConfig.Build(_connection, [new SqlServerTarget(_prod, "a")]);

        Assert.Contains("physical_memory_kb", text, StringComparison.Ordinal);
        Assert.Contains("metric_name: sqlserver.os.memory.total", text, StringComparison.Ordinal);
        Assert.Contains("sys.dm_os_host_info", text, StringComparison.Ordinal);
        Assert.Contains("host_distribution FROM sys.dm_os_host_info), '') AS os_name", text, StringComparison.Ordinal);
        Assert.Contains("host_release FROM sys.dm_os_host_info), '') AS os_release", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ManagedInstance_LeavesOutWhatAzureRunsForIt()
    {
        var instance = Server(
            new Guid("0195a000-0000-7000-8000-000000000003"),
            "sqlmi-01",
            "sqlmi-01.abc.database.windows.net",
            SqlServerEncryption.Required,
            SqlServerPlatform.ManagedInstance);

        var text = CollectorConfig.Build(_connection, [new SqlServerTarget(instance, "a")]);
        var root = Parse(text);
        var suffix = CollectorConfig.ComponentSuffix(instance.Id);

        foreach (var name in new[] { "backup age", "backups", "volumes", "recovery", "log shipping", "cluster nodes" })
        {
            Assert.DoesNotContain($"veyra-agent: {name} ", text, StringComparison.Ordinal);
        }

        Assert.False(((YamlMappingNode)Node(root, "receivers")).Children.ContainsKey($"sqlquery/backups/{suffix}"));

        Assert.Contains("veyra-agent: sessions", text, StringComparison.Ordinal);
        Assert.Contains("veyra-agent: jobs", text, StringComparison.Ordinal);
        Assert.Contains("'managed-instance' AS platform", text, StringComparison.Ordinal);
        Assert.True(((YamlMappingNode)Node(root, "receivers")).Children.ContainsKey($"sqlserver/{suffix}"));
        AssertEveryPipelineReceiverExists(root);
    }

    [Fact]
    public void Build_AzureSqlDatabase_ReadsTheOneDatabaseAndNothingServerWide()
    {
        var database = Server(
            new Guid("0195a000-0000-7000-8000-000000000004"),
            "shop-db",
            "shop.database.windows.net",
            SqlServerEncryption.Required,
            SqlServerPlatform.AzureSqlDatabase,
            "shop");

        var text = CollectorConfig.Build(_connection, [new SqlServerTarget(database, "a")]);
        var root = Parse(text);
        var suffix = CollectorConfig.ComponentSuffix(database.Id);
        var receivers = ((YamlMappingNode)Node(root, "receivers")).Children.Keys
            .Select(key => ((YamlScalarNode)key).Value)
            .ToList();

        Assert.Equal(
            [
                $"sqlquery/{suffix}",
                $"sqlquery/live/{suffix}",
                $"sqlquery/plans/{suffix}",
                $"sqlquery/storage/{suffix}",
                $"sqlquery/azure/{suffix}",
                $"sqlquery/azurestorage/{suffix}",
                $"sqlquery/azureevents/{suffix}",
            ],
            receivers);
        Assert.DoesNotContain("msdb.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("system_health", text, StringComparison.Ordinal);
        Assert.DoesNotContain("&database=master", text, StringComparison.Ordinal);
        Assert.Contains("&database=shop", text, StringComparison.Ordinal);
        Assert.Contains("'azure-sql-database' AS platform", text, StringComparison.Ordinal);
        Assert.Contains(
            $"sqlquery/azureevents/{suffix}",
            ((YamlSequenceNode)Node(root, "service", "pipelines", $"logs/live/{suffix}", "receivers")).Children
                .Select(node => ((YamlScalarNode)node).Value));
        AssertEveryPipelineReceiverExists(root);
    }

    [Fact]
    public void Build_WindowsHostWithEventLogAndIis_AddsEveryHostRead()
    {
        var text = CollectorConfig.Build(
            _connection,
            [new SqlServerTarget(_prod, "a")],
            new HostReads(Windows: true, EventLog: true, Iis: true, Environment: "staging"));
        var root = Parse(text);
        var receivers = Keys(root, "receivers");

        Assert.DoesNotContain("${env:", text, StringComparison.Ordinal);
        Assert.Superset(
            new HashSet<string>
            {
                "hostmetrics",
                "windowseventlog/system",
                "windowseventlog/application",
                "iis",
                "filelog/iis",
                $"sqlserver/{CollectorConfig.ComponentSuffix(_prod.Id)}",
            },
            receivers.ToHashSet());
        Assert.Superset(
            new HashSet<string> { "metrics/host", "logs/eventlog/host", "metrics/iis/host", "logs/iis/host" },
            Keys(root, "service", "pipelines").ToHashSet());
        Assert.Contains("staging", text, StringComparison.Ordinal);
        AssertEveryPipelineReceiverExists(root);
        AssertEveryPipelineProcessorExists(root);
    }

    [Fact]
    public void Build_WindowsHostWithoutEventLogOrIis_ReadsTheHostOnly()
    {
        var root = Parse(CollectorConfig.Build(
            _connection,
            [],
            new HostReads(Windows: true, EventLog: false, Iis: false, Environment: "production")));

        Assert.Equal(["hostmetrics"], Keys(root, "receivers"));
        Assert.Equal(["metrics/host"], Keys(root, "service", "pipelines"));
        AssertEveryPipelineProcessorExists(root);

        // "cpu:" with no value means the scraper's defaults; an empty string instead is refused by the Collector.
        Assert.IsType<YamlMappingNode>(Node(root, "receivers", "hostmetrics", "scrapers", "cpu"));
    }

    [Fact]
    public void Build_LinuxHostWithoutServers_IsNotIdle()
    {
        var root = Parse(CollectorConfig.Build(
            _connection,
            [],
            new HostReads(Windows: false, EventLog: false, Iis: false, Environment: "production")));

        Assert.Equal(["hostmetrics"], Keys(root, "receivers"));
        Assert.Equal("/", Scalar(root, "receivers", "hostmetrics", "root_path"));
        AssertEveryPipelineProcessorExists(root);
    }

    [Fact]
    public void Build_LinuxHostWithDocker_AddsTheContainersStatsAndLogs()
    {
        var text = CollectorConfig.Build(
            _connection,
            [],
            new HostReads(Windows: false, EventLog: false, Iis: false, Environment: "staging", Docker: true));
        var root = Parse(text);

        Assert.DoesNotContain("${env:", text, StringComparison.Ordinal);
        Assert.Equal(["hostmetrics", "docker_stats", "filelog/docker"], Keys(root, "receivers"));
        Assert.Equal(
            ["metrics/host", "metrics/docker/host", "logs/docker/host"],
            Keys(root, "service", "pipelines"));
        Assert.Equal("1.44", Scalar(root, "receivers", "docker_stats", "api_version"));
        Assert.Equal("true", Scalar(root, "receivers", "docker_stats", "metrics", "container.restarts", "enabled"));
        Assert.Equal(
            ["memory_limiter/host", "resourcedetection/host", "resource/host", "batch/host"],
            Sequence(root, "service", "pipelines", "logs/docker/host", "processors"));
        Assert.Contains("staging", text, StringComparison.Ordinal);
        AssertEveryPipelineReceiverExists(root);
        AssertEveryPipelineProcessorExists(root);
    }

    [Fact]
    public void Build_LinuxHostWithDocker_KeepsTheLogLinesExpressionsIntact()
    {
        var root = Parse(CollectorConfig.Build(
            _connection,
            [],
            new HostReads(Windows: false, EventLog: false, Iis: false, Environment: "production", Docker: true)));
        var operators = ((YamlSequenceNode)Node(root, "receivers", "filelog/docker", "operators")).Children
            .Cast<YamlMappingNode>()
            .ToList();

        Assert.Contains(
            operators,
            one => one.Children.TryGetValue(new YamlScalarNode("value"), out var value)
                && ((YamlScalarNode)value).Value == "EXPR(trimSuffix(body, \"\\n\"))");
        Assert.Contains(
            operators,
            one => one.Children.TryGetValue(new YamlScalarNode("to"), out var to)
                && ((YamlScalarNode)to).Value == "resource[\"container.name\"]");
    }

    [Fact]
    public void Build_LinuxHostWithoutDocker_DoesNotReadDocker()
    {
        var root = Parse(CollectorConfig.Build(
            _connection,
            [],
            new HostReads(Windows: false, EventLog: false, Iis: false, Environment: "production", Docker: false)));

        Assert.Equal(["hostmetrics"], Keys(root, "receivers"));
        Assert.Equal(["metrics/host"], Keys(root, "service", "pipelines"));
    }

    [Fact]
    public void Build_WindowsHostWithDocker_DoesNotReadDocker()
    {
        var root = Parse(CollectorConfig.Build(
            _connection,
            [],
            new HostReads(Windows: true, EventLog: false, Iis: false, Environment: "production", Docker: true)));

        Assert.Equal(["hostmetrics"], Keys(root, "receivers"));
        Assert.Equal(["metrics/host"], Keys(root, "service", "pipelines"));
    }

    private static List<string> Keys(YamlMappingNode root, params string[] path) =>
        ((YamlMappingNode)Node(root, path)).Children.Keys.Select(key => ((YamlScalarNode)key).Value!).ToList();

    // The Collector refuses a pipeline that names a processor it does not define.
    private static void AssertEveryPipelineProcessorExists(YamlMappingNode root)
    {
        var processors = Keys(root, "processors").ToHashSet(StringComparer.Ordinal);

        foreach (var (_, pipeline) in ((YamlMappingNode)Node(root, "service", "pipelines")).Children)
        {
            if (((YamlMappingNode)pipeline).Children.TryGetValue(new YamlScalarNode("processors"), out var list))
            {
                foreach (var processor in ((YamlSequenceNode)list).Children)
                {
                    Assert.Contains(((YamlScalarNode)processor).Value!, processors);
                }
            }
        }
    }

    // The Collector refuses a pipeline that names a receiver it does not define.
    private static void AssertEveryPipelineReceiverExists(YamlMappingNode root)
    {
        var receivers = ((YamlMappingNode)Node(root, "receivers")).Children.Keys
            .Select(key => ((YamlScalarNode)key).Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (_, pipeline) in ((YamlMappingNode)Node(root, "service", "pipelines")).Children)
        {
            foreach (var receiver in ((YamlSequenceNode)((YamlMappingNode)pipeline)["receivers"]).Children)
            {
                Assert.Contains(((YamlScalarNode)receiver).Value, receivers);
            }
        }
    }

    private static YamlMappingNode Parse(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));

        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static YamlNode Node(YamlMappingNode root, params string[] path) =>
        path.Aggregate((YamlNode)root, (node, key) => ((YamlMappingNode)node)[key]);

    private static string Scalar(YamlMappingNode root, params string[] path) =>
        ((YamlScalarNode)Node(root, path)).Value ?? "";

    private static string Text(YamlMappingNode node, string key) => ((YamlScalarNode)node[key]).Value ?? "";

    private static string[] Sequence(YamlMappingNode root, params string[] path) =>
        ((YamlSequenceNode)Node(root, path)).Children.Select(child => ((YamlScalarNode)child).Value ?? "").ToArray();
}
