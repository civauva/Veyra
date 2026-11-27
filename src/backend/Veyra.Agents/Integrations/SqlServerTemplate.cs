using YamlDotNet.RepresentationModel;

namespace Veyra.Agents.Integrations;

/// <summary>One version of the SQL Server template, as its header states it (ADR 0028).</summary>
/// <param name="Version">Its version, such as <c>0.2.0</c>.</param>
/// <param name="CollectorCompatibility">The Collector it is tested with, such as <c>otelcol-contrib 0.140</c>.</param>
/// <param name="MinimumSqlServer">The oldest SQL Server it reads, such as <c>SQL Server 2019</c>.</param>
/// <param name="Root">The template itself.</param>
internal sealed record SqlServerTemplate(
    string Version,
    string CollectorCompatibility,
    string MinimumSqlServer,
    YamlMappingNode Root);
