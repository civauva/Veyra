using System.Text.RegularExpressions;
using Veyra.Agents.Collector;
using Veyra.Agents.Integrations;
using Veyra.Agents.Probes;
using Veyra.Agents.SqlServers;
using YamlDotNet.RepresentationModel;

namespace Veyra.Agents.Tests.Collector;

/// <summary>
/// What ADR 0028 adds to an agent's configuration: connections, modules, template versions, and probes.
/// </summary>
public partial class CollectorConfigIntegrationTests
{
    private static readonly AgentConnection _connection = new(new Uri("http://veyra:8080"), "vyk_agentkey");

    private static readonly SqlServer _prod = new(
        Id: new Guid("0195a000-0000-7000-8000-000000000001"),
        Name: "sql-prod-01",
        Address: "10.0.0.5",
        Port: 1433,
        Environment: "production",
        AgentId: Guid.Empty,
        Login: "veyra_monitor",
        Encryption: SqlServerEncryption.Required,
        CreatedAt: DateTimeOffset.UnixEpoch,
        ChangedAt: DateTimeOffset.UnixEpoch);

    private static readonly string _suffix = CollectorConfig.ComponentSuffix(_prod.Id);

    [Fact]
    public void Build_NamedInstance_ConnectsThroughTheBrowserWithoutAPort()
    {
        var server = _prod with { Address = "sql01.corp.local", Instance = "SQLEXPRESS" };

        var root = Parse(CollectorConfig.Build(_connection, [new SqlServerTarget(server, "s3cret")]));

        Assert.Equal(
            "sqlserver://veyra_monitor:s3cret@sql01.corp.local/SQLEXPRESS?encrypt=true&TrustServerCertificate=false" +
            "&app+name=veyra-collector&database=master",
            Scalar(root, "receivers", $"sqlserver/{_suffix}", "datasource"));
        Assert.Equal(
            Scalar(root, "receivers", $"sqlserver/{_suffix}", "datasource"),
            Scalar(root, "receivers", $"sqlquery/live/{_suffix}", "datasource"));
    }

    [Fact]
    public void Build_WindowsNtlm_SendsTheDomainLoginEscaped()
    {
        var server = _prod with { Authentication = SqlServerAuthentication.Ntlm, Login = @"CORP\veyra" };

        var root = Parse(CollectorConfig.Build(_connection, [new SqlServerTarget(server, "p@ss")]));

        Assert.StartsWith(
            "sqlserver://CORP%5Cveyra:p%40ss@10.0.0.5:1433?",
            Scalar(root, "receivers", $"sqlquery/{_suffix}", "datasource"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_IntegratedWindows_LeavesOutTheLoginAndPassword()
    {
        var server = _prod with { Authentication = SqlServerAuthentication.Integrated, Login = "" };

        var root = Parse(CollectorConfig.Build(_connection, [new SqlServerTarget(server, "")]));

        Assert.Equal(
            "sqlserver://10.0.0.5:1433?encrypt=true&TrustServerCertificate=false&app+name=veyra-collector" +
            "&database=master",
            Scalar(root, "receivers", $"sqlserver/{_suffix}", "datasource"));
    }

    [Fact]
    public void Build_EveryModuleAtItsDefault_IsTheTemplateWordForWord()
    {
        var defaults = CollectorConfig.Build(_connection, [new SqlServerTarget(_prod, "a")]);

        var chosen = CollectorConfig.Build(
            _connection,
            [new SqlServerTarget(_prod with { Modules = SqlServerModules.Defaults() }, "a")]);

        Assert.Equal(defaults, chosen);
        Assert.Contains("collection_interval: 15s", defaults, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_PreviousVersion_ReadsWithItsTemplate()
    {
        var current = CollectorConfig.Build(_connection, [new SqlServerTarget(_prod, "a")]);

        var previous = CollectorConfig.Build(
            _connection,
            [new SqlServerTarget(_prod with { TemplateVersion = SqlServerTemplates.Previous.Version }, "a")]);

        // 0.2.0 changed only the template's header; what the agent reads is the same.
        Assert.Equal(current, previous);
    }

    [Fact]
    public void Build_ModuleSwitchedOff_LeavesOutItsReceiversAndPipelineEntries()
    {
        var server = _prod with { Modules = With("activity", enabled: false) };

        var root = Parse(CollectorConfig.Build(_connection, [new SqlServerTarget(server, "a")]));

        Assert.DoesNotContain($"sqlquery/live/{_suffix}", Keys(root, "receivers"));
        Assert.DoesNotContain(
            $"sqlquery/live/{_suffix}",
            Sequence(root, "service", "pipelines", $"logs/live/{_suffix}", "receivers"));
        Assert.Contains($"sqlquery/plans/{_suffix}", Keys(root, "receivers"));
        AssertEveryPipelineReceiverExists(root);
    }

    [Fact]
    public void Build_StorageSwitchedOff_KeepsReadingThePermissions()
    {
        var server = _prod with { Modules = With("storage", enabled: false) };

        var root = Parse(CollectorConfig.Build(_connection, [new SqlServerTarget(server, "a")]));

        var queries = (YamlSequenceNode)Node(root, "receivers", $"sqlquery/storage/{_suffix}", "queries");
        var sql = Assert.Single(queries.Children.Cast<YamlMappingNode>(), _ => true)["sql"];
        Assert.Contains("veyra-agent: permissions", ((YamlScalarNode)sql).Value, StringComparison.Ordinal);
        Assert.Equal("5m", Scalar(root, "receivers", $"sqlquery/storage/{_suffix}", "collection_interval"));
    }

    [Fact]
    public void Build_CoreAtThirtySeconds_KeepsEachReceiversRatio()
    {
        var server = _prod with { Modules = With("core", enabled: true, seconds: 30) };

        var root = Parse(CollectorConfig.Build(_connection, [new SqlServerTarget(server, "a")]));

        Assert.Equal("30s", Scalar(root, "receivers", $"sqlserver/{_suffix}", "collection_interval"));
        Assert.Equal("2m", Scalar(root, "receivers", $"sqlquery/{_suffix}", "collection_interval"));
        Assert.Equal("2m", Scalar(root, "receivers", $"sqlquery/server/{_suffix}", "collection_interval"));
        Assert.Equal("10s", Scalar(root, "receivers", $"sqlquery/live/{_suffix}", "collection_interval"));
    }

    [Fact]
    public void Build_QueriesEveryHour_ReadsPlansAndQueryStoreHourly()
    {
        var server = _prod with { Modules = With("queries", enabled: true, seconds: 3600) };

        var root = Parse(CollectorConfig.Build(_connection, [new SqlServerTarget(server, "a")]));

        Assert.Equal("1h", Scalar(root, "receivers", $"sqlquery/plans/{_suffix}", "collection_interval"));
        Assert.Equal("1h", Scalar(root, "receivers", $"sqlquery/querystore/{_suffix}", "collection_interval"));
    }

    [Fact]
    public void Build_ProbeWithoutServers_AddsItsReceiverAndPipelineWithEverythingItNeeds()
    {
        var probe = new ProbeTarget(
            new Guid("0195a000-0000-7000-8000-0000000000aa"),
            _prod.Connection with { Address = "sql-new.corp.local" },
            "s3cret");
        var id = CollectorConfig.ComponentSuffix(probe.Id);

        var text = CollectorConfig.Build(_connection, [], probes: [probe]);
        var root = Parse(text);

        Assert.Equal([$"sqlquery/probe/{id}"], Keys(root, "receivers"));
        Assert.Equal([$"logs/probe/{id}"], Keys(root, "service", "pipelines"));
        Assert.Equal(
            ["memory_limiter", "batch/live"],
            Sequence(root, "service", "pipelines", $"logs/probe/{id}", "processors"));
        Assert.Equal(["otlphttp"], Sequence(root, "service", "pipelines", $"logs/probe/{id}", "exporters"));
        Assert.StartsWith(
            "sqlserver://veyra_monitor:s3cret@sql-new.corp.local:1433?",
            Scalar(root, "receivers", $"sqlquery/probe/{id}", "datasource"),
            StringComparison.Ordinal);
        Assert.Contains($"'{id}' AS probe", text, StringComparison.Ordinal);
        Assert.Equal("5s", Scalar(root, "receivers", $"sqlquery/probe/{id}", "collection_interval"));
        Assert.DoesNotContain("${env:", text, StringComparison.Ordinal);
        AssertEveryPipelineProcessorExists(root);
        AssertEveryPipelineReceiverExists(root);
    }

    [Fact]
    public void Build_ProbeBesideAServer_KeepsTheServersReadsAsTheyWere()
    {
        var probe = new ProbeTarget(Guid.CreateVersion7(), _prod.Connection, "s3cret");

        var without = Parse(CollectorConfig.Build(_connection, [new SqlServerTarget(_prod, "a")]));
        var with = Parse(CollectorConfig.Build(_connection, [new SqlServerTarget(_prod, "a")], probes: [probe]));

        Assert.Equal(
            Keys(without, "receivers").Append($"sqlquery/probe/{CollectorConfig.ComponentSuffix(probe.Id)}"),
            Keys(with, "receivers"));
        Assert.Equal(Keys(without, "processors"), Keys(with, "processors"));
    }

    [Fact]
    public void Reads_StorageOff_ListsThePermissionsReadAndNoOtherStorageRead()
    {
        var reads = CollectorConfig.Reads(_prod with { Modules = With("storage", enabled: false) });

        Assert.Equal(["permissions"], reads.Where(read => read.Module == "storage").Select(read => read.Name));
        Assert.Contains(reads, read => read.Name == "statement statistics" && read.Module == "core");
        Assert.Contains(reads, read => read.Name == "sessions" && read.Module == "activity");
    }

    [Fact]
    public void MetricModules_TemplateMetrics_BelongToTheModuleOfTheirReceiver()
    {
        var modules = CollectorConfig.MetricModules(_prod);

        Assert.Equal("storage", modules["sqlserver.file.size"]);
        Assert.Equal("jobs", modules["sqlserver.agent.job.running"]);
        Assert.Equal("core", modules["sqlserver.statement.elapsed"]);
        Assert.Equal("storage", modules["sqlserver.login.permission.missing"]);
    }

    [Fact]
    public void Templates_CurrentAndPrevious_StateTheirVersionCollectorAndSqlServer()
    {
        Assert.Equal("0.2.0", SqlServerTemplates.Current.Version);
        Assert.Equal("0.1.0", SqlServerTemplates.Previous.Version);
        Assert.Equal("otelcol-contrib 0.140", SqlServerTemplates.Current.CollectorCompatibility);
        Assert.Equal("SQL Server 2019", SqlServerTemplates.Current.MinimumSqlServer);
        Assert.Null(SqlServerTemplates.Find("0.0.9"));
        Assert.Same(SqlServerTemplates.Previous, SqlServerTemplates.Resolve("0.0.9"));
        Assert.Same(SqlServerTemplates.Current, SqlServerTemplates.Resolve(null));
    }

    [Fact]
    public void Modules_EveryTemplateReceiver_BelongsToAModuleWhoseDefaultIsItsFirstReceiversInterval()
    {
        foreach (var template in new[] { SqlServerTemplates.Current, SqlServerTemplates.Previous })
        {
            var receivers = (YamlMappingNode)template.Root["receivers"];

            foreach (var (name, _) in receivers.Children)
            {
                Assert.True(SqlServerModules.Names(((YamlScalarNode)name).Value!), $"{name} has no module.");
            }

            foreach (var module in SqlServerModules.All)
            {
                var first = module.Receivers
                    .Select(receiver => new YamlScalarNode(receiver))
                    .First(receivers.Children.ContainsKey);
                var interval = ((YamlScalarNode)((YamlMappingNode)receivers[first])["collection_interval"]).Value!;

                Assert.Equal(module.DefaultIntervalSeconds, Seconds(interval));
                Assert.Contains(module.DefaultIntervalSeconds, module.Choices);
            }
        }
    }

    [Fact]
    public void Probe_PermissionsItChecks_AreThoseTheTemplatesCheck()
    {
        var root = Directory.GetParent(AppContext.BaseDirectory)!;

        while (!Directory.Exists(Path.Combine(root.FullName, "integrations", "sqlserver")))
        {
            root = root.Parent!;
        }

        var folder = Path.Combine(root.FullName, "integrations", "sqlserver");
        var probe = File.ReadAllText(Path.Combine(folder, "probe.yaml"));
        var server = PermissionsQuery(File.ReadAllText(Path.Combine(folder, "collector.yaml")));
        var azure = PermissionsQuery(File.ReadAllText(Path.Combine(folder, "azure-sql-database.yaml")));

        Assert.Equal(
            Permissions(server).Concat(Permissions(azure)).Order(StringComparer.Ordinal),
            Permissions(probe).Order(StringComparer.Ordinal));
    }

    private static List<SqlServerModuleChoice> With(string id, bool enabled, int? seconds = null) =>
        SqlServerModules.Defaults()
            .Select(choice => choice.Id == id
                ? choice with { Enabled = enabled, IntervalSeconds = seconds ?? choice.IntervalSeconds }
                : choice)
            .ToList();

    private static int Seconds(string interval) =>
        int.Parse(interval[..^1], System.Globalization.CultureInfo.InvariantCulture)
        * (interval[^1] switch { 'm' => 60, 'h' => 3600, _ => 1 });

    private static string PermissionsQuery(string template)
    {
        var start = template.IndexOf("veyra-agent: permissions", StringComparison.Ordinal);

        return template[start..template.IndexOf("AS p(permission", start, StringComparison.Ordinal)];
    }

    private static IEnumerable<string> Permissions(string sql) =>
        PermissionLiteral().Matches(sql).Select(match => match.Groups["name"].Value);

    [GeneratedRegex(@"\(N'(?<name>[^']+)'")]
    private static partial Regex PermissionLiteral();

    private static List<string> Keys(YamlMappingNode root, params string[] path) =>
        ((YamlMappingNode)Node(root, path)).Children.Keys.Select(key => ((YamlScalarNode)key).Value!).ToList();

    private static void AssertEveryPipelineProcessorExists(YamlMappingNode root)
    {
        var processors = Keys(root, "processors").ToHashSet(StringComparer.Ordinal);

        foreach (var (_, pipeline) in ((YamlMappingNode)Node(root, "service", "pipelines")).Children)
        {
            foreach (var processor in ((YamlSequenceNode)((YamlMappingNode)pipeline)["processors"]).Children)
            {
                Assert.Contains(((YamlScalarNode)processor).Value!, processors);
            }
        }
    }

    private static void AssertEveryPipelineReceiverExists(YamlMappingNode root)
    {
        var receivers = Keys(root, "receivers").ToHashSet(StringComparer.Ordinal);

        foreach (var (_, pipeline) in ((YamlMappingNode)Node(root, "service", "pipelines")).Children)
        {
            foreach (var receiver in ((YamlSequenceNode)((YamlMappingNode)pipeline)["receivers"]).Children)
            {
                Assert.Contains(((YamlScalarNode)receiver).Value!, receivers);
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

    private static string[] Sequence(YamlMappingNode root, params string[] path) =>
        ((YamlSequenceNode)Node(root, path)).Children.Select(child => ((YamlScalarNode)child).Value ?? "").ToArray();
}
