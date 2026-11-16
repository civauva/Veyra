using System.Globalization;
using Microsoft.Extensions.Options;
using Veyra.Core;
using Veyra.Core.Settings;

namespace Veyra.Ingestion.Settings;

/// <summary>
/// The Ingestion module's settings on the Settings screen: how much telemetry may wait to be written before senders
/// are told to retry (ADR 0017).
/// </summary>
/// <param name="rules">The rules <see cref="IngestionOptions"/> must meet.</param>
internal sealed class IngestionSettings(IEnumerable<IValidateOptions<IngestionOptions>> rules)
    : ModuleSettings<IngestionOptions>(VeyraOptionNames.Ingestion, rules)
{
    private static readonly IngestionOptions _defaults = new();

    /// <inheritdoc />
    public override IReadOnlyList<SettingDefinition> Definitions { get; } =
    [
        Queue("MaxQueueLength", "Log records waiting", "log records", _defaults.MaxQueueLength),
        Queue("MaxMetricQueueLength", "Metric points waiting", "metric points", _defaults.MaxMetricQueueLength),
        Queue("MaxSpanQueueLength", "Spans waiting", "spans", _defaults.MaxSpanQueueLength),
    ];

    private static SettingDefinition Queue(string property, string label, string name, int length) => new(
        $"{VeyraOptionNames.Ingestion}:{property}",
        SettingSection.Ingestion,
        "Queues",
        label,
        $"The most {name} held in memory while they wait to be written. When the queue is full, senders are answered "
        + "503 and retry; a longer queue rides out longer database pauses but uses more memory.",
        SettingKind.WholeNumber,
        length.ToString(CultureInfo.InvariantCulture))
    {
        Unit = name,
        Minimum = 1,
        Maximum = 10_000_000,
        Advanced = true,
    };
}
