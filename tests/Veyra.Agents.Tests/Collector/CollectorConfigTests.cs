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
            "&app+name=veyra-collector",
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

    private static SqlServer Server(Guid id, string name, string address, SqlServerEncryption encryption) =>
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
            ChangedAt: DateTimeOffset.UnixEpoch);

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
