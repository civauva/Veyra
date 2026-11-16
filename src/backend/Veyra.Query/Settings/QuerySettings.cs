using System.Globalization;
using Microsoft.Extensions.Options;
using Veyra.Core;
using Veyra.Core.Settings;

namespace Veyra.Query.Settings;

/// <summary>
/// The Query module's settings on the Settings screen: how many rows one request may ask for (ADR 0017).
/// </summary>
/// <param name="rules">The rules <see cref="QueryOptions"/> must meet.</param>
internal sealed class QuerySettings(IEnumerable<IValidateOptions<QueryOptions>> rules)
    : ModuleSettings<QueryOptions>(VeyraOptionNames.Query, rules)
{
    /// <inheritdoc />
    public override IReadOnlyList<SettingDefinition> Definitions { get; } =
    [
        new(
            $"{VeyraOptionNames.Query}:MaxPageSize",
            SettingSection.Advanced,
            "Paging",
            "Rows per request at most",
            "The most log records or traces one request may ask for. Larger pages answer fewer requests but take "
            + "longer each.",
            SettingKind.WholeNumber,
            new QueryOptions().MaxPageSize.ToString(CultureInfo.InvariantCulture))
        {
            Unit = "rows",
            Minimum = 1,
            Maximum = 100_000,
            Advanced = true,
        },
    ];
}
