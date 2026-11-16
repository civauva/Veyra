using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Veyra.Core.Settings;

/// <summary>
/// A module's settings bound from one section to one options type, checked with that type's rules. Modules whose
/// changes need no notes on the review use it as is.
/// </summary>
/// <typeparam name="T">The module's options type.</typeparam>
/// <param name="section">The section the options bind from, such as <c>Veyra:Query</c>.</param>
/// <param name="rules">The rules the options must meet.</param>
public abstract class ModuleSettings<T>(string section, IEnumerable<IValidateOptions<T>> rules) : IModuleSettings
    where T : class, new()
{
    /// <inheritdoc />
    public abstract IReadOnlyList<SettingDefinition> Definitions { get; }

    /// <inheritdoc />
    public IReadOnlyList<SettingProblem> Validate(IConfiguration candidate) =>
        SettingChecks.Check(section, candidate, rules);

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<SettingNote>> ReviewAsync(
        IReadOnlyList<SettingChange> changes,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SettingNote>>([]);
}
