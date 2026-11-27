using System.Globalization;
using System.Text.RegularExpressions;
using Veyra.Agents.Integrations;
using Veyra.Agents.PostgresServers;
using Veyra.Agents.Probes;
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
/// Each server is read with its own template version, and each receiver with its module's switch and interval
/// (ADR 0028). Veyra writes each SQL Server datasource itself, since a named instance or Windows authentication
/// changes its shape. A connection probe adds one receiver and pipeline named after the probe's ID.
/// </para>
/// <para>
/// Components that hold nothing of a server, such as <c>batch</c> and the exporter, are shared. The same input
/// always gives the same text, so its hash only changes when the configuration does.
/// </para>
/// </remarks>
internal static partial class CollectorConfig
{
    private const string _azureResource = "Veyra.Agents.Templates.azure-sql-database.yaml";

    // The reads that run whatever module they sit in, such as the login's permissions, which every screen needs to
    // say why it is empty (ADR 0028).
    private static readonly HashSet<string> _alwaysRun = new(StringComparer.Ordinal) { "permissions" };

    // The template's variables that are the same for every server of an agent. Every other variable names a server.
    private static readonly HashSet<string> _agentVariables = new(StringComparer.Ordinal)
    {
        "VEYRA_OTLP_ENDPOINT",
        "VEYRA_INGESTION_KEY",
    };

    // What an Azure SQL Database reads beyond the template's reads that apply there (VEY-57).
    private static readonly Lazy<YamlMappingNode> _azure = new(() => LoadTemplate(_azureResource));

    // The connection probe (ADR 0028), from integrations/sqlserver/probe.yaml.
    private static readonly Lazy<YamlMappingNode> _probe =
        new(() => LoadTemplate("Veyra.Agents.Templates.sqlserver-probe.yaml"));

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
    /// <param name="probes">The connection probes the agent runs now (ADR 0028), oldest first.</param>
    /// <returns>The configuration as YAML, ready to send over OpAMP.</returns>
    public static string Build(
        AgentConnection connection,
        IReadOnlyList<SqlServerTarget> servers,
        HostReads? host = null,
        IReadOnlyList<PostgresServerTarget>? postgres = null,
        IReadOnlyList<ProbeTarget>? probes = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(servers);

        postgres ??= [];
        probes ??= [];

        var root = servers.Count == 0 && host is null && postgres.Count == 0 && probes.Count == 0
            ? Idle()
            : ForServers(connection, servers, host, postgres, probes);

        var writer = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        writer.WriteLine("# Written by Veyra for this agent (ADR 0019). Veyra replaces it whenever it changes.");
        new YamlStream(new YamlDocument(root)).Save(writer, assignAnchors: false);

        return writer.ToString();
    }

    /// <summary>The part of a component's name that tells which server it reads.</summary>
    /// <param name="serverId">The server's ID.</param>
    /// <returns>The ID as 32 hexadecimal digits.</returns>
    public static string ComponentSuffix(Guid serverId) => serverId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>
    /// The reads a server's agent runs, by their <c>/* veyra-agent: name */</c>, with the module and receiver of
    /// each: the same choice <see cref="Build"/> makes, for the integration page's active checks (ADR 0028).
    /// </summary>
    /// <param name="server">The server.</param>
    /// <returns>The reads, in the template's order.</returns>
    public static IReadOnlyList<CollectorRead> Reads(SqlServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        var reads = new List<CollectorRead>();

        foreach (var (name, body) in ServerReceivers(server))
        {
            if (!((YamlMappingNode)body).Children.TryGetValue(new YamlScalarNode("queries"), out var queries))
            {
                continue;
            }

            foreach (var query in ((YamlSequenceNode)queries).Children.Cast<YamlMappingNode>())
            {
                if (PlatformReads.QueryNameOf(Text(query["sql"])) is { Length: > 0 } read)
                {
                    reads.Add(new CollectorRead(read, SqlServerModules.ModuleOf(name).Id, name));
                }
            }
        }

        return reads;
    }

    /// <summary>The metric names each module's reads send, from the server's template (ADR 0028).</summary>
    /// <param name="server">The server.</param>
    /// <returns>The module of each metric name the template's queries name.</returns>
    public static IReadOnlyDictionary<string, string> MetricModules(SqlServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        var modules = new Dictionary<string, string>(StringComparer.Ordinal);
        var template = SqlServerTemplates.Resolve(server.TemplateVersion).Root;
        var receivers = Mapping(template, "receivers").Children.Concat(Mapping(_azure.Value, "receivers").Children);

        foreach (var (name, body) in receivers)
        {
            var module = SqlServerModules.ModuleOf(Text(name)).Id;

            if (((YamlMappingNode)body).Children.TryGetValue(new YamlScalarNode("queries"), out var queries))
            {
                foreach (var query in ((YamlSequenceNode)queries).Children.Cast<YamlMappingNode>())
                {
                    if (!query.Children.TryGetValue(new YamlScalarNode("metrics"), out var metrics))
                    {
                        continue;
                    }

                    foreach (var metric in ((YamlSequenceNode)metrics).Children.Cast<YamlMappingNode>())
                    {
                        modules.TryAdd(Text(metric["metric_name"]), module);
                    }
                }
            }
        }

        return modules;
    }

    private static YamlMappingNode ForServers(
        AgentConnection connection,
        IReadOnlyList<SqlServerTarget> servers,
        HostReads? host,
        IReadOnlyList<PostgresServerTarget> postgres,
        IReadOnlyList<ProbeTarget> probes)
    {
        var current = SqlServerTemplates.Current.Root;
        var shared = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["VEYRA_OTLP_ENDPOINT"] = connection.VeyraUrl.GetLeftPart(UriPartial.Authority)
                + connection.VeyraUrl.AbsolutePath.TrimEnd('/'),
            ["VEYRA_INGESTION_KEY"] = connection.Key,
        };

        var receivers = new YamlMappingNode();
        var processors = new YamlMappingNode();
        var pipelines = new YamlMappingNode();

        // Shared processors keep their names; each server's own copies follow them. A server on another template
        // version must share the same ones.
        AddSharedProcessors(current, shared, processors);

        foreach (var target in servers)
        {
            var template = SqlServerTemplates.Resolve(target.Server.TemplateVersion).Root;
            var perServerProcessors = PerServerProcessors(template);
            var suffix = ComponentSuffix(target.Server.Id);
            var values = new Dictionary<string, string>(shared, StringComparer.Ordinal);

            foreach (var (key, value) in ServerValues(target))
            {
                values[key] = value;
            }

            AddSharedProcessors(template, shared, processors);

            var platform = target.Server.Platform;
            var azure = platform == SqlServerPlatform.AzureSqlDatabase;
            var datasource = SqlServerDatasource.For(target.Server.Connection, target.Password);
            var read = new HashSet<string>(StringComparer.Ordinal);

            // The template's receivers with the queries this platform runs and the modules switched on, each at its
            // module's interval; one left with nothing to read is left out.
            foreach (var (name, body) in ServerReceivers(target.Server))
            {
                receivers.Add($"{name}/{suffix}", WithDatasource(Fill(body, values), datasource));
                read.Add(name);
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

        foreach (var probe in probes)
        {
            AddProbe(probe, shared, receivers, pipelines);
        }

        var exporters = (YamlMappingNode)Fill(Mapping(current, "exporters"), shared);

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

        foreach (var value in ConnectionValues(server.Connection, target.Password))
        {
            yield return value;
        }

        yield return ("SQLSERVER_NAME", server.Name);
        yield return ("VEYRA_ENVIRONMENT", server.Environment);
    }

    // The template's connection variables. Veyra writes each datasource itself (SqlServerDatasource), but every
    // variable of a template needs a value, and the login and platform appear in queries too.
    private static IEnumerable<(string Key, string Value)> ConnectionValues(
        SqlServerConnection connection,
        string password)
    {
        // The login and password sit inside a URL. Escaping them also removes every '$', so the Collector never
        // reads part of a password as a variable.
        yield return ("SQLSERVER_USER", Uri.EscapeDataString(connection.Login));
        yield return ("SQLSERVER_PASSWORD", Uri.EscapeDataString(password));
        yield return ("SQLSERVER_HOST", connection.Address);
        yield return ("SQLSERVER_PORT", connection.Port.ToString(CultureInfo.InvariantCulture));
        yield return ("SQLSERVER_ENCRYPT", connection.Encryption == SqlServerEncryption.Off ? "false" : "true");
        yield return (
            "SQLSERVER_TRUST_CERT",
            connection.Encryption == SqlServerEncryption.TrustServerCertificate ? "true" : "false");

        // A server is read from master; an Azure SQL Database from itself, the only database its user may enter.
        yield return ("SQLSERVER_DATABASE", Uri.EscapeDataString(connection.Database ?? "master"));
        yield return ("SQLSERVER_PLATFORM", PlatformReads.Name(connection.Platform));
    }

    // The receivers a server's agent runs, by their name in the template, unfilled: the template's and, for an Azure
    // SQL Database, azure-sql-database.yaml's, each with the queries its platform runs and its module's switch and
    // interval. A receiver left with nothing to read is left out.
    private static IEnumerable<(string Name, YamlMappingNode Body)> ServerReceivers(SqlServer server)
    {
        var template = SqlServerTemplates.Resolve(server.TemplateVersion).Root;
        var platform = server.Platform;
        var receivers = Mapping(template, "receivers").Children
            .Where(pair => PlatformReads.Reads(platform, Text(pair.Key)))
            .Select(pair => (Name: Text(pair.Key), Body: Keep(pair.Value, platform)));

        if (platform == SqlServerPlatform.AzureSqlDatabase)
        {
            receivers = receivers.Concat(
                Mapping(_azure.Value, "receivers").Children.Select(pair =>
                    (Name: Text(pair.Key), Body: (YamlMappingNode?)pair.Value)));
        }

        foreach (var (name, body) in receivers)
        {
            if (body is not null && Shape(name, body, server.Choices) is { } shaped)
            {
                yield return (name, shaped);
            }
        }
    }

    // A receiver as its module's choice wants it (ADR 0028). Switched off, it keeps only the reads that always run,
    // at the template's interval, or is left out. At another interval than the module's default, each receiver keeps
    // its ratio to the module's first one: Core at 30 seconds reads the sqlserver receiver every 30 seconds and the
    // minute's queries every 2 minutes. At the default, the receiver is the template's, word for word.
    private static YamlMappingNode? Shape(
        string name,
        YamlMappingNode receiver,
        IReadOnlyList<SqlServerModuleChoice> choices)
    {
        var module = SqlServerModules.ModuleOf(name);
        var choice = SqlServerModules.ChoiceOf(choices, module);

        if (!choice.Enabled && !module.Required)
        {
            return AlwaysRun(receiver);
        }

        if (choice.IntervalSeconds == module.DefaultIntervalSeconds
            || !receiver.Children.TryGetValue(new YamlScalarNode("collection_interval"), out var interval))
        {
            return receiver;
        }

        var seconds = Seconds(Text(interval)) * (double)choice.IntervalSeconds / module.DefaultIntervalSeconds;

        return new YamlMappingNode(receiver.Children.Select(pair => Text(pair.Key) == "collection_interval"
            ? new KeyValuePair<YamlNode, YamlNode>(pair.Key, new YamlScalarNode(Interval(seconds)))
            : pair));
    }

    // A receiver of a module that is off, with only its reads that always run, or null when it has none.
    private static YamlMappingNode? AlwaysRun(YamlMappingNode receiver)
    {
        if (!receiver.Children.TryGetValue(new YamlScalarNode("queries"), out var queries))
        {
            return null;
        }

        var kept = ((YamlSequenceNode)queries).Children
            .Where(query => _alwaysRun.Contains(PlatformReads.QueryNameOf(Text(((YamlMappingNode)query)["sql"]))))
            .ToList();

        if (kept.Count == 0)
        {
            return null;
        }

        var copy = new YamlMappingNode(receiver.Children.Where(pair => Text(pair.Key) != "queries"));
        copy.Add("queries", new YamlSequenceNode(kept));

        return copy;
    }

    // A Collector duration as seconds: "15s", "60s", "5m", "1h".
    private static double Seconds(string duration)
    {
        var unit = duration[^1] switch
        {
            's' => 1,
            'm' => 60,
            'h' => 3600,
            _ => throw new InvalidOperationException($"The template holds an interval Veyra cannot read: {duration}."),
        };

        return double.Parse(duration[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture) * unit;
    }

    // Seconds as the shortest Collector duration that says them exactly, at least one second.
    private static string Interval(double seconds)
    {
        var whole = Math.Max(1, (long)Math.Round(seconds));

        return whole % 3600 == 0 ? string.Create(CultureInfo.InvariantCulture, $"{whole / 3600}h")
            : whole % 60 == 0 ? string.Create(CultureInfo.InvariantCulture, $"{whole / 60}m")
            : string.Create(CultureInfo.InvariantCulture, $"{whole}s");
    }

    // A filled-in receiver with the datasource Veyra writes for the connection, quoted as the filled-in one was.
    private static YamlMappingNode WithDatasource(YamlNode filled, string datasource) =>
        new(((YamlMappingNode)filled).Children.Select(pair => Text(pair.Key) == "datasource"
            ? new KeyValuePair<YamlNode, YamlNode>(
                pair.Key,
                new YamlScalarNode(datasource) { Style = ScalarStyle.DoubleQuoted })
            : pair));

    // The processors of a template that name the server, which each server gets a copy of, in the template's order.
    private static List<string> PerServerProcessors(YamlMappingNode template) =>
        Mapping(template, "processors").Children
            .Where(processor => NamesTheServer(processor.Value))
            .Select(processor => Text(processor.Key))
            .ToList();

    private static void AddSharedProcessors(
        YamlMappingNode template,
        IReadOnlyDictionary<string, string> shared,
        YamlMappingNode processors)
    {
        var perServer = PerServerProcessors(template);

        foreach (var (name, body) in Mapping(template, "processors").Children)
        {
            if (!perServer.Contains(Text(name)))
            {
                AddOnce(processors, Text(name), Fill(body, shared));
            }
        }
    }

    // One connection probe (ADR 0028): the probe's receiver and pipeline from integrations/sqlserver/probe.yaml,
    // named after the probe's ID, so its answer and its errors are told apart from every server's. Its pipeline uses
    // the SQL Server template's shared processors and exporter.
    private static void AddProbe(
        ProbeTarget probe,
        IReadOnlyDictionary<string, string> shared,
        YamlMappingNode receivers,
        YamlMappingNode pipelines)
    {
        var template = _probe.Value;
        var suffix = ComponentSuffix(probe.Id);
        var values = new Dictionary<string, string>(shared, StringComparer.Ordinal) { ["VEYRA_PROBE_ID"] = suffix };

        foreach (var (key, value) in ConnectionValues(probe.Connection, probe.Password))
        {
            values[key] = value;
        }

        var datasource = SqlServerDatasource.For(probe.Connection, probe.Password);

        foreach (var (name, body) in Mapping(template, "receivers").Children)
        {
            receivers.Add($"{Text(name)}/{suffix}", WithDatasource(Fill(body, values), datasource));
        }

        foreach (var (name, body) in Mapping(Mapping(template, "service"), "pipelines").Children)
        {
            var pipeline = (YamlMappingNode)body;

            pipelines.Add($"{Text(name)}/{suffix}", new YamlMappingNode
            {
                { "receivers", Names(pipeline, "receivers", _ => true, suffix) },
                { "processors", Names(pipeline, "processors", _ => false, suffix) },
                { "exporters", Names(pipeline, "exporters", _ => false, suffix) },
            });
        }
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
