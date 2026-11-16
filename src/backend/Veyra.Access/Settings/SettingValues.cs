using System.Globalization;
using Veyra.Core.Settings;

namespace Veyra.Access.Settings;

/// <summary>
/// Reads a value as the Settings screen sends it, checks it against its definition's kind and limits, and writes it the
/// way configuration does, so a saved value always binds.
/// </summary>
internal static class SettingValues
{
    /// <summary>Normalizes a value for its definition.</summary>
    /// <param name="definition">The setting.</param>
    /// <param name="raw">
    /// The value as sent, in configuration form: a ratio from 0 to 1, a duration as d.hh:mm:ss.
    /// </param>
    /// <returns>The value as configuration writes it, or why it cannot be used.</returns>
    public static (string? Value, string? Problem) Normalize(SettingDefinition definition, string raw)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(raw);

        var text = raw.Trim();

        if (text.Length == 0)
        {
            return definition.Optional || definition.Kind == SettingKind.Text
                ? (string.Empty, null)
                : (null, "This needs a value.");
        }

        return definition.Kind switch
        {
            SettingKind.WholeNumber => long.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var whole)
                ? InRange(definition, whole, whole.ToString(CultureInfo.InvariantCulture))
                : (null, "Enter a whole number."),
            SettingKind.Number => double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var number)
                ? InRange(definition, number, number.ToString(CultureInfo.InvariantCulture))
                : (null, "Enter a number."),
            SettingKind.Ratio => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio)
                ? InRange(definition, ratio * 100, ratio.ToString(CultureInfo.InvariantCulture))
                : (null, "Enter a percentage."),
            SettingKind.Duration => TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var span)
                ? InRange(definition, Shown(span, definition.Unit), span.ToString("c", CultureInfo.InvariantCulture))
                : (null, "Enter a length of time."),
            SettingKind.Switch => bool.TryParse(text, out var on)
                ? (on ? "true" : "false", null)
                : (null, "Choose on or off."),
            SettingKind.Choice => definition.Choices.FirstOrDefault(
                choice => choice.Equals(text, StringComparison.OrdinalIgnoreCase)) is { } chosen
                    ? (chosen, null)
                    : (null, $"Choose one of {string.Join(", ", definition.Choices)}."),
            _ => (text, null),
        };
    }

    // A duration in the unit the screen shows it in.
    private static double Shown(TimeSpan span, string? unit) => unit switch
    {
        "min" => span.TotalMinutes,
        "h" => span.TotalHours,
        "days" => span.TotalDays,
        _ => span.TotalSeconds,
    };

    private static (string? Value, string? Problem) InRange(SettingDefinition definition, double shown, string value)
    {
        var unit = definition.Unit is null ? string.Empty : " " + definition.Unit;

        if (definition.Minimum is { } minimum && shown < minimum)
        {
            return (null, $"Must be at least {minimum.ToString(CultureInfo.InvariantCulture)}{unit}.");
        }

        if (definition.Maximum is { } maximum && shown > maximum)
        {
            return (null, $"Must be at most {maximum.ToString(CultureInfo.InvariantCulture)}{unit}.");
        }

        return (value, null);
    }
}
