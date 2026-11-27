using System.Globalization;

namespace Veyra.Core.Alerts;

/// <summary>
/// An incident as other modules read it (ADR 0026): which alerts it groups and when it was open, so the Query module
/// can gather what surrounds it.
/// </summary>
/// <param name="Id">The incident's ID.</param>
/// <param name="Number">Its number, which people say: INC-42.</param>
/// <param name="Environment">The environment of its alerts, or <see langword="null"/> when they name none.</param>
/// <param name="OpenedAt">When its first alert opened.</param>
/// <param name="ResolvedAt">When its last alert resolved; <see langword="null"/> while any is open.</param>
/// <param name="Alerts">Its alerts, in the order they joined, each with its rule and subject.</param>
public sealed record IncidentState(
    Guid Id,
    long Number,
    string? Environment,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ResolvedAt,
    IReadOnlyList<AlertState> Alerts)
{
    /// <summary>The key people see and say, such as <c>INC-42</c>.</summary>
    public string Key => KeyOf(Number);

    /// <summary>The key of an incident number.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The key, such as <c>INC-42</c>.</returns>
    public static string KeyOf(long number) => string.Create(CultureInfo.InvariantCulture, $"INC-{number}");
}
