using Microsoft.Extensions.Configuration;

namespace Veyra.Core.Settings;

/// <summary>
/// A module's settings, as the Settings screen needs them: what they are, whether a candidate value is valid, and what
/// a change will do. Every module registers one; the Access module serves them all (ADR 0017).
/// </summary>
/// <remarks>
/// The module still reads its settings from configuration, through <c>IOptionsMonitor</c>, so a saved change reaches
/// it without either module knowing about the other.
/// </remarks>
public interface IModuleSettings
{
    /// <summary>Every setting the module has. A bound options property without a definition fails a test.</summary>
    IReadOnlyList<SettingDefinition> Definitions { get; }

    /// <summary>
    /// Checks a candidate configuration with the module's own validators, the same ones that run at startup.
    /// </summary>
    /// <param name="candidate">The whole configuration as it would be after the change.</param>
    /// <returns>Every problem found; empty when the module accepts the candidate.</returns>
    IReadOnlyList<SettingProblem> Validate(IConfiguration candidate);

    /// <summary>
    /// Says what the changes to this module's settings will do, such as how much data a shorter retention deletes.
    /// </summary>
    /// <param name="changes">The changes to this module's settings only.</param>
    /// <param name="cancellationToken">Cancels any query the review needs.</param>
    /// <returns>A note per change worth saying something about; empty when there is nothing to say.</returns>
    Task<IReadOnlyList<SettingNote>> ReviewAsync(
        IReadOnlyList<SettingChange> changes,
        CancellationToken cancellationToken);
}
