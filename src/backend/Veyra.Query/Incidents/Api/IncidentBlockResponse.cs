using Veyra.Core.Alerts;

namespace Veyra.Query.Incidents.Api;

/// <summary>A block around an incident, in the shape the alert list gives its blocks (ADR 0026).</summary>
/// <param name="Kind"><c>service</c>, <c>host</c>, <c>database</c>, or <c>container</c>.</param>
/// <param name="Name">The block name.</param>
/// <param name="Part">For a container, the host it runs on; otherwise <see langword="null"/>.</param>
/// <param name="Id">The block's identity on the home, for example <c>host:production/web-01</c>.</param>
public sealed record IncidentBlockResponse(string Kind, string Name, string? Part, string Id)
{
    /// <summary>The block of a subject in an environment.</summary>
    /// <param name="subject">The subject, whose part is kept only for a container.</param>
    /// <param name="environment">The environment.</param>
    /// <returns>The block.</returns>
    public static IncidentBlockResponse Of(AlertSubject subject, string? environment)
    {
        ArgumentNullException.ThrowIfNull(subject);

        return new(
            subject.KindName,
            subject.Name,
            subject.Kind == AlertBlockKind.Container ? subject.Part : null,
            subject.BlockId(environment));
    }
}
