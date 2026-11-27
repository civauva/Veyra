using Veyra.Core.Settings;

namespace Veyra.Query;

/// <summary>
/// What <see cref="QueryOptions"/> must meet, at startup and before the Settings screen saves a change.
/// </summary>
public sealed class QueryOptionsRules : OptionsRules<QueryOptions>
{
    /// <inheritdoc />
    protected override IEnumerable<(bool Holds, string Message)> Rules(QueryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        yield return (options.MaxPageSize > 0, "Veyra:Query:MaxPageSize must be greater than zero.");
        yield return (
            options.SilentAfterHosts >= TimeSpan.FromMinutes(1),
            "Veyra:Query:SilentAfterHosts must be at least one minute.");
        yield return (
            options.SilentAfterServices >= TimeSpan.FromMinutes(1),
            "Veyra:Query:SilentAfterServices must be at least one minute.");
        yield return (
            options.LiveListeners is > 0 and <= 10_000,
            "Veyra:Query:LiveListeners must be between 1 and 10000.");
        yield return (
            options.MaxExportRecords is > 0 and <= 10_000_000,
            "Veyra:Query:MaxExportRecords must be between 1 and 10000000.");
    }
}
