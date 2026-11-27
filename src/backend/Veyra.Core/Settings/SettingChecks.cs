using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Veyra.Core.Settings;

/// <summary>
/// Checks a candidate configuration with a module's own options validators, the same ones that run at startup, so a
/// value the Settings screen accepts is a value the module accepts (ADR 0017).
/// </summary>
public static partial class SettingChecks
{
    /// <summary>
    /// Binds the section of a candidate configuration to a fresh options object and runs every validator on it.
    /// </summary>
    /// <remarks>
    /// Veyra's validation messages start with the setting's key, for example "Veyra:Access:LockoutThreshold must be
    /// greater than zero.", so a problem is tied to the key its message names.
    /// </remarks>
    /// <typeparam name="T">The module's options type.</typeparam>
    /// <param name="section">The section the options bind from, such as <c>Veyra:Storage</c>.</param>
    /// <param name="candidate">The configuration as it would be after the change.</param>
    /// <param name="validators">The module's validators for <typeparamref name="T"/>.</param>
    /// <returns>Every problem found; empty when the candidate is valid.</returns>
    public static IReadOnlyList<SettingProblem> Check<T>(
        string section,
        IConfiguration candidate,
        IEnumerable<IValidateOptions<T>> validators)
        where T : class, new()
    {
        ArgumentException.ThrowIfNullOrEmpty(section);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(validators);

        var options = new T();

        try
        {
            candidate.GetSection(section).Bind(options);
        }
        catch (InvalidOperationException exception)
        {
            return [new SettingProblem(UnreadableKey(section, exception), "This value is not of the right kind.")];
        }

        return validators
            .Select(validator => validator.Validate(Options.DefaultName, options))
            .Where(result => result.Failed)
            .SelectMany(result => result.Failures ?? [])
            .Select(message => new SettingProblem(KeyOf(message), message))
            .ToList();
    }

    // "Veyra:Test:WarningDays must not be above …" names the key first; other messages name none. A key may end in a
    // rule name with hyphens, such as Veyra:Alerting:Runbooks:error-rate (ADR 0026).
    private static string? KeyOf(string message) => LeadingKey().Match(message) is { Success: true } match
        ? match.Groups["key"].Value
        : null;

    // The binder says "Failed to convert configuration value '…' at 'Veyra:Test:CautionDays' to type …".
    private static string? UnreadableKey(string section, InvalidOperationException exception) =>
        BinderPath().Match(exception.Message) is { Success: true } match ? match.Groups["key"].Value : section;

    [GeneratedRegex(@"^(?<key>Veyra(:[A-Za-z0-9]+(-[A-Za-z0-9]+)*)+)\b")]
    private static partial Regex LeadingKey();

    [GeneratedRegex(@"at '(?<key>[^']+)'")]
    private static partial Regex BinderPath();
}
