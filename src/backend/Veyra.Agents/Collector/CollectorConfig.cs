using System.Globalization;
using System.Text.RegularExpressions;
using Veyra.Agents.PostgresServers;
using Veyra.Agents.SqlServers;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Veyra.Agents.Collector;

/// <summary>
/// Writes an agent's whole Collector configuration from the tested templates in <c>integrations/sqlserver</c> and
/// <c>integrations/postgres</c>, with every value filled in, so nothing is left to environment variables on the agent
/// (ADR 0019).
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

    private const string _azureResource = "Veyra.Agents.Templates.azure-sql-database.yaml";

    // The template's variables that are the same for every server of an agent. Every other variable names a server.
    private static readonly HashSet<string> _agentVariables = new(StringComparer.Ordinal)
    {
        "VEYRA_OTLP_ENDPOINT",
        "VEYRA_INGESTION_KEY",
    };

    private static readonly Lazy<YamlMappingNode> _template = new(() => LoadTemplate(_templateResource));

    // What an Azure SQL Database reads beyond the template's reads that apply there (VEY-57).
    private static readonly Lazy<YamlMappingNode> _azure = new(() => LoadTemplate(_azureResource));

    // What an agent reads of its own host (VEY-35): Windows, with IIS beside it when asked, or Linux.
    private static readonly Lazy<YamlMappingNode> _windows =
        new(() => LoadTemplate("Veyra.Agents.Templates.windows.yaml"));

    private static readonly Lazy<YamlMappingNode> _iis = new(() => LoadTemplate("Veyra.Agents.Templates.iis.yaml"));

    private static readonly Lazy<YamlMappingNode> _linux = new(() => LoadTemplate("Veyra.Agents.Templates.linux.yaml"));

    // A Linux host's Docker containers (VEY-32), beside the Linux host's reads.
    private static readonly Lazy<YamlMappingNode> _docker =
        new(() => LoadTemplate("Veyra.Agents.Templates.docker.yaml"));

    // What a PostgreSQL server reads (VEY-33), from integrations/postgres.
    private static readonly Lazy<YamlMappingNode> _postgres =
        new(() => LoadTemplate("Veyra.Agents.Templates.postgres.yaml"));

    // Variables that fill a number or a switch, which the Collector refuses as quoted text. Veyra fills them itself
    // from checked values, so they are written plain.
    private static readonly HashSet<string> _typedVariables = new(StringComparer.Ordinal)
    {
        "POSTGRES_PORT",
        "POSTGRES_TLS_INSECURE",
        "POSTGRES_TLS_SKIP_VERIFY",
    };

    /// <summary>
    /// Builds the configuration for one agent.
    /// </summary>
    /// <param name="connection">Where the Collector sends what it reads, and its key.</param>
    /// <param name="servers">The servers the agent reads, in the order they are listed.</param>
    /// <param name="host">
    /// What the agent reads of the host it runs on (VEY-35), or <see langword="null"/> when it does not watch it.
    /// </param>
    /// <param name="postgres">The PostgreSQL servers the agent reads (VEY-33), in the order they are listed.</param>
    /// <returns>The configuration as YAML, ready to send over OpAMP.</returns>
    public static string Build(
        AgentConnection connection,
        IReadOnlyList<SqlServerTarget> servers,
        HostReads? host = null,
        IReadOnlyList<PostgresServerTarget>? postgres = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(servers);

        postgres ??= [];

        var root = servers.Count == 0 && host is null && postgres.Count == 0
            ? Idle()
            : ForServers(connection, servers, host, postgres);

        var writer = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        writer.WriteLine("# Written by Veyra for this agent (ADR 0019). Veyra replaces it whenever it changes.");
        new YamlStream(new YamlDocument(root)).Save(writer, assignAnchors: false);

        return writer.ToString();
    }

    /// <summary>The part of a component's name that tells which server it reads.</summary>
    /// <param name="serverId">The server's ID.</param>
    /// <returns>The ID as 32 hexadecimal digits.</returns>
    public static string ComponentSuffix(Guid serverId) => serverId.ToString("N", CultureInfo.InvariantCulture);

    private static YamlMappingNode ForServers(
        AgentConnection connection,
        IReadOnlyList<SqlServerTarget> servers,
        HostReads? host,
        IReadOnlyList<PostgresServerTarget> postgres)
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

            var platform = target.Server.Platform;
            var azure = platform == SqlServerPlatform.AzureSqlDatabase;
            var read = new HashSet<string>(StringComparer.Ordinal);

            // The template's receivers with the queries this platform runs; one left with none is left out.
            foreach (var (name, body) in Mapping(template, "receivers").Children)
            {
                if (PlatformReads.Reads(platform, Text(name)) && Keep(body, platform) is { } kept)
                {
                    receivers.Add($"{Text(name)}/{suffix}", Fill(kept, values));
                    read.Add(Text(name));
                }
            }

            if (azure)
            {
                foreach (var (name, body) in Mapping(_azure.Value, "receivers").Children)
                {
                    receivers.Add($"{Text(name)}/{suffix}", Fill(body, values));
                    read.Add(Text(name));
                }
            }

            foreach (var name in perServerProcessors)
            {
                processors.Add($"{name}/{suffix}", Fill(Mapping(template, "processors")[name], values));
            }

            var azurePipelines = Mapping(Mapping(_azure.Value, "service"), "pipelines");

            foreach (var (name, body) in Mapping(Mapping(template, "service"), "pipelines").Children)
            {
                var pipeline = (YamlMappingNode)body;
                var extra = azure && azurePipelines.Children.TryGetValue(name, out var more)
                    ? List((YamlMappingNode)more, "receivers")
                    : [];
                var names = List(pipeline, "receivers").Concat(extra).Where(read.Contains).ToList();

                if (names.Count == 0)
                {
                    continue;
                }

                var copy = new YamlMappingNode
                {
                    { "receivers", new YamlSequenceNode(names.Select(one => new YamlScalarNode($"{one}/{suffix}"))) },
                    { "processors", Names(pipeline, "processors", perServerProcessors.Contains, suffix) },
                    { "exporters", Names(pipeline, "exporters", _ => false, suffix) },
                };

                pipelines.Add($"{Text(name)}/{suffix}", copy);
            }
        }

        var exporters = (YamlMappingNode)Fill(Mapping(template, "exporters"), shared);

        foreach (var target in postgres)
        {
            AddPostgres(target, shared, receivers, processors, exporters, pipelines);
        }

        if (host is not null)
        {
            AddHost(host, shared, receivers, processors, pipelines);
        }

        return new YamlMappingNode
        {
            { "receivers", receivers },
            { "processors", processors },
            { "exporters", exporters },
            { "service", new YamlMappingNode { { "pipelines", pipelines } } },
        };
    }

    // One PostgreSQL server's reads from integrations/postgres (VEY-33): each receiver, processor that names the
    // server, and pipeline named after the server's ID, as a SQL Server's are. Its other processors and its exporter
    // are the SQL Server template's own: they are kept once, and must be the same.
    private static void AddPostgres(
        PostgresServerTarget target,
        IReadOnlyDictionary<string, string> shared,
        YamlMappingNode receivers,
        YamlMappingNode processors,
        YamlMappingNode exporters,
        YamlMappingNode pipelines)
    {
        var template = _postgres.Value;
        var suffix = ComponentSuffix(target.Server.Id);
        var values = new Dictionary<string, string>(shared, StringComparer.Ordinal);

        foreach (var (key, value) in PostgresValues(target))
        {
            values[key] = value;
        }

        var perServer = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, body) in Mapping(template, "processors").Children)
        {
            if (NamesTheServer(body))
            {
                perServer.Add(Text(name));
                processors.Add($"{Text(name)}/{suffix}", Fill(body, values));
            }
            else
            {
                AddOnce(processors, Text(name), Fill(body, shared));
            }
        }

        foreach (var (name, body) in Mapping(template, "receivers").Children)
        {
            receivers.Add($"{Text(name)}/{suffix}", Fill(body, values));
        }

        foreach (var (name, body) in Mapping(template, "exporters").Children)
        {
            AddOnce(exporters, Text(name), Fill(body, shared));
        }

        foreach (var (name, body) in Mapping(Mapping(template, "service"), "pipelines").Children)
        {
            var pipeline = (YamlMappingNode)body;

            pipelines.Add($"{Text(name)}/{suffix}", new YamlMappingNode
            {
                { "receivers", Names(pipeline, "receivers", _ => true, suffix) },
                { "processors", Names(pipeline, "processors", perServer.Contains, suffix) },
                { "exporters", Names(pipeline, "exporters", _ => false, suffix) },
            });
        }
    }

    // The host's reads from integrations/windows or integrations/linux (VEY-35), and integrations/docker beside Linux
    // (VEY-32). Their processors and pipelines take a "/host" suffix: the SQL Server template has processors of the
    // same names that set other attributes. The Docker configuration also runs alone, so it repeats the Linux
    // host's processors; a repeated one is kept once, and must be the same.
    private static void AddHost(
        HostReads host,
        IReadOnlyDictionary<string, string> shared,
        YamlMappingNode receivers,
        YamlMappingNode processors,
        YamlMappingNode pipelines)
    {
        var values = new Dictionary<string, string>(shared, StringComparer.Ordinal)
        {
            ["VEYRA_ENVIRONMENT"] = host.Environment,
        };
        var parts = host.Windows ? new List<YamlMappingNode> { _windows.Value } : [_linux.Value];

        if (host.Windows && host.Iis)
        {
            parts.Add(_iis.Value);
        }

        if (!host.Windows && host.Docker)
        {
            parts.Add(_docker.Value);
        }

        bool Wanted(string name) => host.EventLog || !name.Contains("eventlog", StringComparison.Ordinal);

        foreach (var part in parts)
        {
            if (part.Children.TryGetValue(new YamlScalarNode("receivers"), out var partReceivers))
            {
                var wanted = ((YamlMappingNode)partReceivers).Children.Where(pair => Wanted(Text(pair.Key)));

                foreach (var (name, body) in wanted)
                {
                    receivers.Add(Text(name), Fill(body, values));
                }
            }

            if (part.Children.TryGetValue(new YamlScalarNode("processors"), out var partProcessors))
            {
                foreach (var (name, body) in ((YamlMappingNode)partProcessors).Children)
                {
                    AddOnce(processors, $"{Text(name)}/host", Fill(body, values));
                }
            }

            var partPipelines = Mapping(Mapping(part, "service"), "pipelines").Children
                .Where(pair => Wanted(Text(pair.Key)));

            foreach (var (name, body) in partPipelines)
            {
                var pipeline = (YamlMappingNode)body;
                var hostProcessors = List(pipeline, "processors").Select(one => new YamlScalarNode($"{one}/host"));

                pipelines.Add($"{Text(name)}/host", new YamlMappingNode
                {
                    { "receivers", Names(pipeline, "receivers", _ => false, "") },
                    { "processors", new YamlSequenceNode(hostProcessors) },
                    { "exporters", Names(pipeline, "exporters", _ => false, "") },
                });
            }
        }
    }

    // Adds a component unless one of its name is already there, which must then be the same: two templates that
    // disagree about a shared processor would otherwise lose one's settings without a word.
    private static void AddOnce(YamlMappingNode components, string name, YamlNode body)
    {
        if (!components.Children.TryGetValue(new YamlScalarNode(name), out var existing))
        {
            components.Add(name, body);

            return;
        }

        if (Yaml(existing) != Yaml(body))
        {
            throw new InvalidOperationException($"The Collector templates define {name} in two different ways.");
        }
    }

    private static string Yaml(YamlNode node)
    {
        var writer = new StringWriter(CultureInfo.InvariantCulture);
        new YamlStream(new YamlDocument(node)).Save(writer, assignAnchors: false);

        return writer.ToString();
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

    private static IEnumerable<(string Key, string Value)> PostgresValues(PostgresServerTarget target)
    {
        var server = target.Server;

        // The postgresql receiver takes the role and password as they are; the sqlquery receiver escapes them for its
        // URL itself. The service allows neither spaces nor quotes in them, which the receiver could not pass on.
        yield return ("POSTGRES_HOST", server.Address);
        yield return ("POSTGRES_PORT", server.Port.ToString(CultureInfo.InvariantCulture));
        yield return ("POSTGRES_USER", server.Login);
        yield return ("POSTGRES_PASSWORD", target.Password);
        yield return ("POSTGRES_SERVER", server.Name);
        yield return ("POSTGRES_TLS_INSECURE", server.Encryption == PostgresEncryption.Off ? "true" : "false");
        yield return (
            "POSTGRES_TLS_SKIP_VERIFY",
            server.Encryption == PostgresEncryption.TrustServerCertificate ? "true" : "false");
        yield return (
            "POSTGRES_SSLMODE",
            server.Encryption switch
            {
                PostgresEncryption.Off => "disable",
                PostgresEncryption.TrustServerCertificate => "require",
                _ => "verify-full",
            });
        yield return ("VEYRA_ENVIRONMENT", server.Environment);
    }

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

        // A server is read from master; an Azure SQL Database from itself, the only database its user may enter.
        yield return ("SQLSERVER_DATABASE", Uri.EscapeDataString(server.Database ?? "master"));
        yield return ("SQLSERVER_PLATFORM", PlatformReads.Name(server.Platform));
    }

    // A receiver with only the queries the platform runs, or null when it runs none of them. A receiver without
    // queries, such as the sqlserver receiver, is kept as it is.
    private static YamlMappingNode? Keep(YamlNode body, SqlServerPlatform platform)
    {
        var receiver = (YamlMappingNode)body;

        if (!receiver.Children.TryGetValue(new YamlScalarNode("queries"), out var queries))
        {
            return receiver;
        }

        var kept = ((YamlSequenceNode)queries).Children
            .Where(query => PlatformReads.Runs(platform, Text(((YamlMappingNode)query)["sql"])))
            .ToList();

        if (kept.Count == 0)
        {
            return null;
        }

        var copy = new YamlMappingNode(receiver.Children.Where(pair => Text(pair.Key) != "queries"));
        copy.Add("queries", new YamlSequenceNode(kept));

        return copy;
    }

    private static IEnumerable<string> List(YamlMappingNode pipeline, string list) =>
        pipeline.Children.TryGetValue(new YamlScalarNode(list), out var node)
            ? ((YamlSequenceNode)node).Children.Select(Text)
            : [];

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
                new YamlScalarNode(Substitute(text, values))
                {
                    Style = IsTyped(text) ? ScalarStyle.Plain : ScalarStyle.DoubleQuoted,
                },

            // A key with no value, such as a scraper's "cpu:", means its defaults. Copied as a scalar it would be
            // written as an empty string, which the Collector refuses; an empty mapping means the same to it.
            YamlScalarNode { Value: null or "", Style: ScalarStyle.Plain or ScalarStyle.Any } => new YamlMappingNode(),
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

    // A scalar that is one typed variable and nothing else, such as "${env:POSTGRES_PORT:-5432}".
    private static bool IsTyped(string text) =>
        Variable().Match(text) is { Success: true } match
        && match.Value == text
        && _typedVariables.Contains(match.Groups["name"].Value);

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

    private static YamlMappingNode LoadTemplate(string resource)
    {
        using var stream = typeof(CollectorConfig).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded resource '{resource}' is missing.");
        using var reader = new StreamReader(stream);

        var yaml = new YamlStream();
        yaml.Load(reader);

        return (YamlMappingNode)yaml.Documents[0].RootNode;
    }

    [GeneratedRegex(@"\$\{env:(?<name>[A-Z0-9_]+)(?::-(?<default>[^}]*))?\}")]
    private static partial Regex Variable();
}
