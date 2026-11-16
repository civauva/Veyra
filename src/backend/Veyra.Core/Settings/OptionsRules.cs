using Microsoft.Extensions.Options;

namespace Veyra.Core.Settings;

/// <summary>
/// The rules an options class must meet, written once and used both at startup and by the Settings screen before a
/// change is saved (ADR 0017).
/// </summary>
/// <typeparam name="T">The options type the rules apply to.</typeparam>
public abstract class OptionsRules<T> : IValidateOptions<T>
    where T : class
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, T options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = Rules(options).Where(rule => !rule.Holds).Select(rule => rule.Message).ToList();

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Every rule with whether it holds for these options.</summary>
    /// <param name="options">The options to check.</param>
    /// <returns>
    /// Each rule and its message. A message starts with the key it is about, such as
    /// "Veyra:Storage:LogRetentionDays must be at least 1.", so the screen can show it next to the field.
    /// </returns>
    protected abstract IEnumerable<(bool Holds, string Message)> Rules(T options);
}
