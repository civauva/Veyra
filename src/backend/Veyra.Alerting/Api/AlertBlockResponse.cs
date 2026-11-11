using Veyra.Core.Alerts;

namespace Veyra.Alerting.Api;

/// <summary>What an alert is about (ADR 0012).</summary>
/// <param name="Kind"><c>service</c> or <c>host</c>.</param>
/// <param name="Name">The service or host name.</param>
/// <param name="Part">The part of the block, for example a disk's mount point; <see langword="null"/> for none.</param>
/// <param name="Id">The block's identity on the home, for example <c>host:production/web-01</c>.</param>
public sealed record AlertBlockResponse(string Kind, string Name, string? Part, string Id)
{
    /// <summary>The block of an alert.</summary>
    /// <param name="alert">The alert.</param>
    /// <returns>The block.</returns>
    public static AlertBlockResponse Of(AlertState alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var subject = alert.Subject;

        return new(subject.KindName, subject.Name, subject.Part, subject.BlockId(alert.Environment));
    }
}
