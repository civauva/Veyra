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
    }
}
