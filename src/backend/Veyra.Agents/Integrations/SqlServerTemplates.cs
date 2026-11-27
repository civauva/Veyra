using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Veyra.Agents.Integrations;

/// <summary>
/// The versions of the SQL Server template Veyra ships (ADR 0028): the current one,
/// <c>integrations/sqlserver/collector.yaml</c>, and the one before it, kept beside it as
/// <c>collector.&lt;version&gt;.yaml</c>. Each states its version, its Collector, and its oldest SQL Server in its
/// header.
/// </summary>
internal static partial class SqlServerTemplates
{
    /// <summary>The name of the integration package the templates make up.</summary>
    public const string PackageName = "veyra-sqlserver-integration";

    private static readonly Lazy<SqlServerTemplate> _current =
        new(() => Load("Veyra.Agents.Templates.sqlserver.yaml"));

    private static readonly Lazy<SqlServerTemplate> _previous =
        new(() => Load("Veyra.Agents.Templates.sqlserver.previous.yaml"));

    /// <summary>The version new servers take and Update moves to.</summary>
    public static SqlServerTemplate Current => _current.Value;

    /// <summary>The version before it, which Roll back moves to.</summary>
    public static SqlServerTemplate Previous => _previous.Value;

    /// <summary>Finds a shipped version.</summary>
    /// <param name="version">The version, such as <c>0.1.0</c>.</param>
    /// <returns>The template, or <see langword="null"/> when Veyra does not ship that version.</returns>
    public static SqlServerTemplate? Find(string? version) =>
        version == Current.Version ? Current
        : version == Previous.Version ? Previous
        : null;

    /// <summary>
    /// The template a server reads with. A version Veyra no longer ships, older than the previous one, reads with the
    /// previous one, as the upgrade moves it (ADR 0028).
    /// </summary>
    /// <param name="version">The server's version; <see langword="null"/> for the current one.</param>
    /// <returns>The template.</returns>
    public static SqlServerTemplate Resolve(string? version) => version is null ? Current : Find(version) ?? Previous;

    /// <summary>Reads a template's header and body.</summary>
    /// <param name="text">The template's text.</param>
    /// <returns>The template.</returns>
    /// <exception cref="InvalidOperationException">The header lacks a line.</exception>
    public static SqlServerTemplate Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string Header(string name) =>
            HeaderLine().Matches(text)
                .Where(match => match.Groups["name"].Value == name)
                .Select(match => match.Groups["value"].Value.Trim())
                .FirstOrDefault()
            ?? throw new InvalidOperationException($"The SQL Server template's header lacks veyra-{name}.");

        var version = Header("template");
        const string kind = "sqlserver ";

        if (!version.StartsWith(kind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The template's header names another kind: {version}.");
        }

        var yaml = new YamlStream();
        yaml.Load(new StringReader(text));

        return new SqlServerTemplate(
            version[kind.Length..].Trim(),
            Header("collector"),
            Header("sql-server"),
            (YamlMappingNode)yaml.Documents[0].RootNode);
    }

    private static SqlServerTemplate Load(string resource)
    {
        using var stream = typeof(SqlServerTemplates).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded resource '{resource}' is missing.");
        using var reader = new StreamReader(stream);

        return Parse(reader.ReadToEnd());
    }

    // A header line such as "# veyra-template: sqlserver 0.2.0", only among the comments at the top.
    [GeneratedRegex(@"^# veyra-(?<name>[a-z-]+): (?<value>.+)$", RegexOptions.Multiline)]
    private static partial Regex HeaderLine();
}
