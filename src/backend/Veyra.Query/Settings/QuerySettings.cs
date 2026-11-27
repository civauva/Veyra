using System.Globalization;
using Microsoft.Extensions.Options;
using Veyra.Core;
using Veyra.Core.Settings;

namespace Veyra.Query.Settings;

/// <summary>
/// The Query module's settings on the Settings screen: how many rows one request may ask for, when the resource
/// lists call something silent, how many live streams an instance serves, and how long a log export may run (ADR
/// 0017, ADR 0022, ADR 0024).
/// </summary>
/// <param name="rules">The rules <see cref="QueryOptions"/> must meet.</param>
internal sealed class QuerySettings(IEnumerable<IValidateOptions<QueryOptions>> rules)
    : ModuleSettings<QueryOptions>(VeyraOptionNames.Query, rules)
{
    private static readonly QueryOptions _defaults = new();

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
            _defaults.MaxPageSize.ToString(CultureInfo.InvariantCulture))
        {
            Unit = "rows",
            Minimum = 1,
            Maximum = 100_000,
            Advanced = true,
        },
        new(
            $"{VeyraOptionNames.Query}:{nameof(QueryOptions.SilentAfterHosts)}",
            SettingSection.Advanced,
            "Inventory",
            "Hosts are silent after",
            "A host, container, or database server that sent nothing for this long stays listed under Resources, "
            + "marked silent and counted as not online.",
            SettingKind.Duration,
            Text(_defaults.SilentAfterHosts))
        {
            Unit = "min",
            Minimum = 1,
            Maximum = 1_440,
            Advanced = true,
        },
        new(
            $"{VeyraOptionNames.Query}:{nameof(QueryOptions.SilentAfterServices)}",
            SettingSection.Advanced,
            "Inventory",
            "Services are silent after",
            "A service that sent no metrics for this long stays listed under Resources, marked silent. Make it longer "
            + "than the gap between runs of a batch job.",
            SettingKind.Duration,
            Text(_defaults.SilentAfterServices))
        {
            Unit = "min",
            Minimum = 1,
            Maximum = 10_080,
            Advanced = true,
        },
        new(
            $"{VeyraOptionNames.Query}:LiveListeners",
            SettingSection.Advanced,
            "Live",
            "Live streams at most",
            "How many browsers may follow Live at once on this instance: the rates in the top bar and new log "
            + "records together. One more is turned away until another closes. Each holds a connection open and up "
            + "to 1,000 waiting records.",
            SettingKind.WholeNumber,
            new QueryOptions().LiveListeners.ToString(CultureInfo.InvariantCulture))
        {
            Unit = "streams",
            Minimum = 1,
            Maximum = 10_000,
            Advanced = true,
        },
        new(
            $"{VeyraOptionNames.Query}:{nameof(QueryOptions.MaxExportRecords)}",
            SettingSection.Advanced,
            "Export",
            "Records per export at most",
            "The most log records one export of the Log Explorer writes, newest first. A longer result is cut off "
            + "here, and the file says so on its last line. A larger export holds a database connection for longer.",
            SettingKind.WholeNumber,
            _defaults.MaxExportRecords.ToString(CultureInfo.InvariantCulture))
        {
            Unit = "records",
            Minimum = 1,
            Maximum = 10_000_000,
            Advanced = true,
        },
    ];

    private static string Text(TimeSpan value) => value.ToString("c", CultureInfo.InvariantCulture);
}
