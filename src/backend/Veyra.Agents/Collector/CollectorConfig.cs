using System.Globalization;
using System.Text.RegularExpressions;
using Veyra.Agents.SqlServers;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Veyra.Agents.Collector;

/// <summary>
/// Writes an agent's whole Collector configuration from the tested template in <c>integrations/sqlserver</c>, with
/// every value filled in, so nothing is left to environment variables on the agent (ADR 0019).
/// </summary>
/// <remarks>
/// <para>
/// The template describes one server. For each server the agent reads, every receiver, every processor that names
/// the server, and every pipeline get a copy named after the server's ID, such as <c>sqlserver/0195…</c>. The
/// Collector's own logs name a failing component by that ID, which is how Veyra tells which server it was.
/// </para>
/// <para>
/// Components that hold nothing of a server, such as <c>batch</c> and the exporter, are shared. The same input
/// always gives the same text, so its hash only changes when the configuration does.
/// </para>
/// </remarks>
internal static partial class CollectorConfig
{
    private const string _templateResource = "Veyra.Agents.Templates.sqlserver.yaml";

    // The template's variables that are the same for every server of an agent. Every other variable names a server.
    private static readonly HashSet<string> _agentVariables = new(StringComparer.Ordinal)
    {
        "VEYRA_OTLP_ENDPOINT",
        "VEYRA_INGESTION_KEY",
    };

    private static readonly Lazy<YamlMappingNode> _template = new(LoadTemplate);

    /// <summary>
    /// Builds the configuration for one agent.
    /// </summary>
    /// <param name="connection">Where the Collector sends what it reads, and its key.</param>
    /// <param name="servers">The servers the agent reads, in the order they are listed.</param>
    /// <returns>The configuration as YAML, ready to send over OpAMP.</returns>
    public static string Build(AgentConnection connection, IReadOnlyList<SqlServerTarget> servers)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(servers);

        var root = servers.Count == 0 ? Idle() : ForServers(connection, servers);

        var writer = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        writer.WriteLine("# Written by Veyra for this agent (ADR 0019). Veyra replaces it whenever it changes.");
        new YamlStream(new YamlDocument(root)).Save(writer, assignAnchors: false);

        return writer.ToString();
    }

    /// <summary>The part of a component's name that tells which server it reads.</summary>
    /// <param name="serverId">The server's ID.</param>
    /// <returns>The ID as 32 hexadecimal digits.</returns>
    public static string ComponentSuffix(Guid serverId) => serverId.ToString("N", CultureInfo.InvariantCulture);

    private static YamlMappingNode ForServers(AgentConnection connection, IReadOnlyList<SqlServerTarget> servers)
    {
        var template = _template.Value;
        var shared = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["VEYRA_OTLP_ENDPOINT"] = connection.VeyraUrl.GetLeftPart(UriPartial.Authority)
                + connection.VeyraUrl.AbsolutePath.TrimEnd('/'),
            ["VEYRA_INGESTION_KEY"] = connection.Key,
        };

        var receivers = new YamlMappingNode();
        var processors = new YamlMappingNode();
        var pipelines = new YamlMappingNode();
        var perServerProcessors = Mapping(template, "processors").Children
            .Where(processor => NamesTheServer(processor.Value))
            .Select(processor => Text(processor.Key))
            .ToHashSet(StringComparer.Ordinal);

        // Shared processors keep their names; each server's own copies follow them.
        foreach (var (name, body) in Mapping(template, "processors").Children)
        {
            if (!perServerProcessors.Contains(Text(name)))
            {
                processors.Add(Text(name), Fill(body, shared));
            }
        }

        foreach (var target in servers)
        {
            var suffix = ComponentSuffix(target.Server.Id);
            var values = new Dictionary<string, string>(shared, StringComparer.Ordinal);

            foreach (var (key, value) in ServerValues(target))
            {
                values[key] = value;
            }

            foreach (var (name, body) in Mapping(template, "receivers").Children)
            {
                receivers.Add($"{Text(name)}/{suffix}", Fill(body, values));
            }

            foreach (var name in perServerProcessors)
            {
                processors.Add($"{name}/{suffix}", Fill(Mapping(template, "processors")[name], values));
            }

            foreach (var (name, body) in Mapping(Mapping(template, "service"), "pipelines").Children)
            {
                var pipeline = (YamlMappingNode)body;
                var copy = new YamlMappingNode
                {
                    { "receivers", Names(pipeline, "receivers", _ => true, suffix) },
                    { "processors", Names(pipeline, "processors", perServerProcessors.Contains, suffix) },
                    { "exporters", Names(pipeline, "exporters", _ => false, suffix) },
                };

                pipelines.Add($"{Text(name)}/{suffix}", copy);
            }
        }

        var exporters = (YamlMappingNode)Fill(Mapping(template, "exporters"), shared);

        return new YamlMappingNode
        {
            { "receivers", receivers },
            { "processors", processors },
            { "exporters", exporters },
            { "service", new YamlMappingNode { { "pipelines", pipelines } } },
        };
    }

    // A Collector needs at least one pipeline. With no server to read, a pipeline that does nothing keeps the agent
    // running and connected, ready for the first server.
    private static YamlMappingNode Idle() =>
        new()
        {
            { "receivers", new YamlMappingNode { { "nop", new YamlMappingNode() } } },
            { "exporters", new YamlMappingNode { { "nop", new YamlMappingNode() } } },
            {
                "service",
                new YamlMappingNode
                {
                    {
                        "pipelines",
                        new YamlMappingNode
                        {
                            {
                                "logs/idle",
                                new YamlMappingNode
                                {
                                    { "receivers", new YamlSequenceNode(new YamlScalarNode("nop")) },
                                    { "exporters", new YamlSequenceNode(new YamlScalarNode("nop")) },
                                }
                            },
                        }
                    },
                }
            },
        };

    private static IEnumerable<(string Key, string Value)> ServerValues(SqlServerTarget target)
    {
        var server = target.Server;

        // The login and password sit inside a URL. Escaping them also removes every '$', so the Collector never
        // reads part of a password as a variable.
        yield return ("SQLSERVER_USER", Uri.EscapeDataString(server.Login));
        yield return ("SQLSERVER_PASSWORD", Uri.EscapeDataString(target.Password));
        yield return ("SQLSERVER_HOST", server.Address);
        yield return ("SQLSERVER_PORT", server.Port.ToString(CultureInfo.InvariantCulture));
        yield return ("SQLSERVER_NAME", server.Name);
        yield return ("SQLSERVER_ENCRYPT", server.Encryption == SqlServerEncryption.Off ? "false" : "true");
        yield return (
            "SQLSERVER_TRUST_CERT",
            server.Encryption == SqlServerEncryption.TrustServerCertificate ? "true" : "false");
        yield return ("VEYRA_ENVIRONMENT", server.Environment);
    }

    private static YamlSequenceNode Names(
        YamlMappingNode pipeline,
        string list,
        Func<string, bool> perServer,
        string suffix)
    {
        var names = pipeline.Children.TryGetValue(new YamlScalarNode(list), out var node)
            ? ((YamlSequenceNode)node).Children.Select(Text)
            : [];

        return new YamlSequenceNode(names.Select(name =>
            (YamlNode)new YamlScalarNode(perServer(name) ? $"{name}/{suffix}" : name)));
    }

    // Copies a node with every ${env:NAME} or ${env:NAME:-default} filled in. A filled-in value is always quoted, so a
    // server named "1433" stays text.
    private static YamlNode Fill(YamlNode node, IReadOnlyDictionary<string, string> values) =>
        node switch
        {
            YamlScalarNode scalar when scalar.Value is { } text && text.Contains("${env:", StringComparison.Ordinal) =>
                new YamlScalarNode(Substitute(text, values)) { Style = ScalarStyle.DoubleQuoted },
            YamlScalarNode scalar => new YamlScalarNode(scalar.Value)
            {
                // Folded and literal blocks become one line, which the emitter may write in any style.
                Style = scalar.Style is ScalarStyle.Folded or ScalarStyle.Literal ? ScalarStyle.Any : scalar.Style,
            },
            YamlSequenceNode sequence => new YamlSequenceNode(sequence.Children.Select(child => Fill(child, values))),
            YamlMappingNode mapping => new YamlMappingNode(mapping.Children.Select(pair =>
                new KeyValuePair<YamlNode, YamlNode>(Fill(pair.Key, values), Fill(pair.Value, values)))),
            _ => throw new InvalidOperationException($"The Collector template holds an unexpected node: {node}."),
        };

    private static string Substitute(string text, IReadOnlyDictionary<string, string> values) =>
        Variable().Replace(text, match =>
        {
            var name = match.Groups["name"].Value;

            if (values.TryGetValue(name, out var value))
            {
                // The Collector reads "$$" as a plain "$" and anything else after "$" as a reference.
                return value.Replace("$", "$$", StringComparison.Ordinal);
            }

            return match.Groups["default"].Success
                ? match.Groups["default"].Value
                : throw new InvalidOperationException($"The Collector template needs {name}, which Veyra lacks.");
        });

    private static bool NamesTheServer(YamlNode node) =>
        node switch
        {
            YamlScalarNode scalar => scalar.Value is { } text
                && Variable().Matches(text).Any(match => !_agentVariables.Contains(match.Groups["name"].Value)),
            YamlSequenceNode sequence => sequence.Children.Any(NamesTheServer),
            YamlMappingNode mapping => mapping.Children.Any(pair => NamesTheServer(pair.Value)),
            _ => false,
        };

    private static YamlMappingNode Mapping(YamlMappingNode node, string key) => (YamlMappingNode)node[key];

    private static string Text(YamlNode node) => ((YamlScalarNode)node).Value ?? "";

    private static YamlMappingNode LoadTemplate()
    {
        using var stream = typeof(CollectorConfig).Assembly.GetManifestResourceStream(_templateResource)
            ?? throw new InvalidOperationException($"Embedded resource '{_templateResource}' is missing.");
        using var reader = new StreamReader(stream);

        var yaml = new YamlStream();
        yaml.Load(reader);

        return (YamlMappingNode)yaml.Documents[0].RootNode;
    }

    [GeneratedRegex(@"\$\{env:(?<name>[A-Z0-9_]+)(?::-(?<default>[^}]*))?\}")]
    private static partial Regex Variable();
}
