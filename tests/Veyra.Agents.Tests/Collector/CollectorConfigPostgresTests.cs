using Veyra.Agents.Collector;
using Veyra.Agents.PostgresServers;
using Veyra.Agents.SqlServers;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Veyra.Agents.Tests.Collector;

public class CollectorConfigPostgresTests
{
    private static readonly AgentConnection _connection = new(new Uri("http://veyra:8080"), "vyk_agentkey");

    private static readonly PostgresServer _main = new(
        Id: new Guid("0195a000-0000-7000-8000-0000000000a1"),
        Name: "pg-main",
        Address: "10.0.0.5",
        Port: 5432,
        Environment: "production",
        AgentId: Guid.Empty,
        Login: "veyra_monitor",
        Encryption: PostgresEncryption.Required,
        CreatedAt: DateTimeOffset.UnixEpoch,
        ChangedAt: DateTimeOffset.UnixEpoch);

    private static readonly SqlServer _sql = new(
        Id: new Guid("0195a000-0000-7000-8000-000000000001"),
        Name: "sql-prod-01",
        Address: "10.0.0.6",
        Port: 1433,
        Environment: "production",
        AgentId: Guid.Empty,
        Login: "veyra_monitor",
        Encryption: SqlServerEncryption.Required,
        CreatedAt: DateTimeOffset.UnixEpoch,
        ChangedAt: DateTimeOffset.UnixEpoch);

    private static readonly string _suffix = CollectorConfig.ComponentSuffix(_main.Id);

    [Fact]
    public void Build_OnePostgresServer_FillsInEveryValue()
    {
        var text = CollectorConfig.Build(_connection, [], postgres: [new PostgresServerTarget(_main, "s3cret")]);
        var root = Parse(text);

        Assert.DoesNotContain("${env:", text, StringComparison.Ordinal);
        Assert.Equal("10.0.0.5:5432", Scalar(root, "receivers", $"postgresql/{_suffix}", "endpoint"));
        Assert.Equal("veyra_monitor", Scalar(root, "receivers", $"postgresql/{_suffix}", "username"));
        Assert.Equal("s3cret", Scalar(root, "receivers", $"postgresql/{_suffix}", "password"));
        Assert.Equal("10.0.0.5", Scalar(root, "receivers", $"sqlquery/{_suffix}", "host"));
        Assert.Equal("5432", Scalar(root, "receivers", $"sqlquery/{_suffix}", "port"));
        Assert.Equal("postgres", Scalar(root, "receivers", $"sqlquery/{_suffix}", "database"));
        Assert.Equal("s3cret", Scalar(root, "receivers", $"sqlquery/{_suffix}", "password"));
        Assert.Equal("http://veyra:8080", Scalar(root, "exporters", "otlphttp", "endpoint"));
    }

    [Fact]
    public void Build_OnePostgresServer_WritesThePortAsANumber()
    {
        // The Collector refuses the text "5432" where it expects a number, and "true" where it expects a switch.
        var root = Parse(CollectorConfig.Build(_connection, [], postgres: [new PostgresServerTarget(_main, "x")]));

        Assert.Equal(ScalarStyle.Plain, ScalarNode(root, "receivers", $"sqlquery/{_suffix}", "port").Style);
        Assert.Equal(
            ScalarStyle.Plain,
            ScalarNode(root, "receivers", $"postgresql/{_suffix}", "tls", "insecure").Style);
        Assert.Equal(
            ScalarStyle.Plain,
            ScalarNode(root, "receivers", $"postgresql/{_suffix}", "tls", "insecure_skip_verify").Style);
    }

    [Fact]
    public void Build_OnePostgresServer_NamesTheServerAndItsEnvironment()
    {
        var root = Parse(CollectorConfig.Build(
            _connection,
            [],
            postgres: [new PostgresServerTarget(_main with { Environment = "staging" }, "x")]));

        var attributes = (YamlSequenceNode)Find(root, "processors", $"resource/{_suffix}", "attributes");
        var values = attributes.Children
            .Cast<YamlMappingNode>()
            .ToDictionary(attribute => Text(attribute, "key"), attribute => Text(attribute, "value"));

        Assert.Equal("postgresql", values["db.system.name"]);
        Assert.Equal("pg-main", values["server.address"]);
        Assert.Equal("staging", values["deployment.environment.name"]);
        Assert.Equal(
            [$"metrics/{_suffix}", $"metrics/queries/{_suffix}"],
            Keys(root, "service", "pipelines"));
        Assert.Equal(
            ["memory_limiter", $"resource/{_suffix}", "batch"],
            Sequence(root, "service", "pipelines", $"metrics/{_suffix}", "processors"));
        Assert.Equal(
            [$"sqlquery/{_suffix}"],
            Sequence(root, "service", "pipelines", $"metrics/queries/{_suffix}", "receivers"));
    }

    [Theory]
    [InlineData(PostgresEncryption.Required, "false", "false", "verify-full")]
    [InlineData(PostgresEncryption.TrustServerCertificate, "false", "true", "require")]
    [InlineData(PostgresEncryption.Off, "true", "false", "disable")]
    public void Build_Encryption_SetsBothReceiversAlike(
        PostgresEncryption encryption,
        string insecure,
        string skipVerify,
        string sslMode)
    {
        var server = _main with { Encryption = encryption };

        var root = Parse(CollectorConfig.Build(_connection, [], postgres: [new PostgresServerTarget(server, "x")]));

        Assert.Equal(insecure, Scalar(root, "receivers", $"postgresql/{_suffix}", "tls", "insecure"));
        Assert.Equal(skipVerify, Scalar(root, "receivers", $"postgresql/{_suffix}", "tls", "insecure_skip_verify"));
        Assert.Equal(sslMode, Scalar(root, "receivers", $"sqlquery/{_suffix}", "additional_params", "sslmode"));
    }

    [Fact]
    public void Build_PasswordWithADollar_IsNotReadAsAVariable()
    {
        var text = CollectorConfig.Build(
            _connection,
            [],
            postgres: [new PostgresServerTarget(_main, "p@ss:w$rd/1&x=${env:HOME}")]);

        // The Collector reads "$$" as one "$"; the sqlquery receiver escapes the password for its URL itself.
        Assert.Equal(
            "p@ss:w$$rd/1&x=$${env:HOME}",
            Scalar(Parse(text), "receivers", $"postgresql/{_suffix}", "password"));
        Assert.DoesNotContain(
            "${env:",
            text.Replace("$${env:", "", StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_SqlServerAndPostgres_KeepsTheirComponentsApartAndSharesTheRest()
    {
        var text = CollectorConfig.Build(
            _connection,
            [new SqlServerTarget(_sql, "a")],
            new HostReads(Windows: false, EventLog: false, Iis: false, Environment: "production", Docker: true),
            [new PostgresServerTarget(_main, "b")]);
        var root = Parse(text);
        var sql = CollectorConfig.ComponentSuffix(_sql.Id);

        Assert.DoesNotContain("${env:", text, StringComparison.Ordinal);
        Assert.Superset(
            new HashSet<string>
            {
                $"sqlserver/{sql}",
                $"sqlquery/{sql}",
                $"postgresql/{_suffix}",
                $"sqlquery/{_suffix}",
                "docker_stats",
            },
            Keys(root, "receivers").ToHashSet());
        Assert.Equal("microsoft.sql_server", ResourceValue(root, $"resource/{sql}", "db.system.name"));
        Assert.Equal("postgresql", ResourceValue(root, $"resource/{_suffix}", "db.system.name"));
        Assert.Single(Keys(root, "processors"), name => name == "batch");
        Assert.Equal(["otlphttp"], Keys(root, "exporters"));
        AssertEveryPipelineComponentExists(root);
    }

    [Fact]
    public void Build_TwoPostgresServers_GivesEachItsOwnReceivers()
    {
        var other = _main with { Id = new Guid("0195a000-0000-7000-8000-0000000000a2"), Name = "pg-replica" };

        var root = Parse(CollectorConfig.Build(
            _connection,
            [],
            postgres: [new PostgresServerTarget(_main, "a"), new PostgresServerTarget(other, "b")]));
        var second = CollectorConfig.ComponentSuffix(other.Id);

        Assert.Equal(
            [$"postgresql/{_suffix}", $"sqlquery/{_suffix}", $"postgresql/{second}", $"sqlquery/{second}"],
            Keys(root, "receivers"));
        Assert.Equal("pg-replica", ResourceValue(root, $"resource/{second}", "server.address"));
        AssertEveryPipelineComponentExists(root);
    }

    [Fact]
    public void Build_SamePostgresInput_GivesTheSameText()
    {
        PostgresServerTarget[] targets = [new(_main, "a")];

        Assert.Equal(
            CollectorConfig.Build(_connection, [], postgres: targets),
            CollectorConfig.Build(_connection, [], postgres: targets));
    }

    private static string ResourceValue(YamlMappingNode root, string processor, string key) =>
        ((YamlSequenceNode)Find(root, "processors", processor, "attributes")).Children
            .Cast<YamlMappingNode>()
            .Where(attribute => Text(attribute, "key") == key)
            .Select(attribute => Text(attribute, "value"))
            .Single();

    // The Collector refuses a pipeline that names a component it does not define.
    private static void AssertEveryPipelineComponentExists(YamlMappingNode root)
    {
        foreach (var (_, pipeline) in ((YamlMappingNode)Find(root, "service", "pipelines")).Children)
        {
            foreach (var kind in new[] { "receivers", "processors", "exporters" })
            {
                var defined = Keys(root, kind).ToHashSet(StringComparer.Ordinal);

                if (((YamlMappingNode)pipeline).Children.TryGetValue(new YamlScalarNode(kind), out var list))
                {
                    foreach (var component in ((YamlSequenceNode)list).Children)
                    {
                        Assert.Contains(((YamlScalarNode)component).Value!, defined);
                    }
                }
            }
        }
    }

    private static List<string> Keys(YamlMappingNode root, params string[] path) =>
        ((YamlMappingNode)Find(root, path)).Children.Keys.Select(key => ((YamlScalarNode)key).Value!).ToList();

    private static YamlMappingNode Parse(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));

        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static YamlNode Find(YamlMappingNode root, params string[] path) =>
        path.Aggregate((YamlNode)root, (node, key) => ((YamlMappingNode)node)[key]);

    private static YamlScalarNode ScalarNode(YamlMappingNode root, params string[] path) =>
        (YamlScalarNode)Find(root, path);

    private static string Scalar(YamlMappingNode root, params string[] path) => ScalarNode(root, path).Value ?? "";

    private static string Text(YamlMappingNode node, string key) => ((YamlScalarNode)node[key]).Value ?? "";

    private static string[] Sequence(YamlMappingNode root, params string[] path) =>
        ((YamlSequenceNode)Find(root, path)).Children.Select(child => ((YamlScalarNode)child).Value ?? "").ToArray();
}
