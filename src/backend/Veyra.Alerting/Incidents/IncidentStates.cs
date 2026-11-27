namespace Veyra.Alerting.Incidents;

/// <summary>The names of <see cref="IncidentStatus"/>, as the database stores them and the API answers them.</summary>
internal static class IncidentStates
{
    /// <summary>Spells a status: <c>open</c>, <c>acknowledged</c>, or <c>resolved</c>.</summary>
    /// <param name="status">The status.</param>
    /// <returns>Its name.</returns>
    public static string NameOf(IncidentStatus status) => status switch
    {
        IncidentStatus.Acknowledged => "acknowledged",
        IncidentStatus.Resolved => "resolved",
        _ => "open",
    };

    /// <summary>Reads a status by its name, in any case.</summary>
    /// <param name="name">The name.</param>
    /// <param name="status">The status, when the name is one.</param>
    /// <returns><see langword="true"/> when the name is a status.</returns>
    public static bool TryParse(string? name, out IncidentStatus status)
    {
        foreach (var candidate in Enum.GetValues<IncidentStatus>())
        {
            if (string.Equals(name, NameOf(candidate), StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;

                return true;
            }
        }

        status = IncidentStatus.Open;

        return false;
    }
}
