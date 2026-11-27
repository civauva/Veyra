namespace Veyra.Storage;

/// <summary>
/// How the span writer recognises a job run in the traces (ADR 0027), bound from the <c>Veyra:Storage:Jobs</c>
/// section.
/// </summary>
public sealed class JobOptions
{
    /// <summary>
    /// The span attributes that name a job, separated by commas, first match first. A root span that carries one of
    /// them is a run of that job. Defaults to the OpenTelemetry CI/CD convention and the attribute Quartz.NET, Hangfire
    /// integrations, and most hand-made instrumentation use. Empty recognises no traced jobs.
    /// </summary>
    public string NameAttributes { get; set; } = "cicd.pipeline.name,job.name";

    /// <summary>The attributes of <see cref="NameAttributes"/>, trimmed, in order, blanks left out.</summary>
    /// <returns>The attribute names.</returns>
    public IReadOnlyList<string> NameAttributeList() =>
        [.. (NameAttributes ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)];
}
