using Veyra.Core.Settings;

namespace Veyra.Agents.Tests;

/// <summary>Says which settings an Admin saved, from the keys a test gives it.</summary>
internal sealed class FakeSavedSettings : ISavedSettings
{
    /// <summary>The saved keys.</summary>
    public List<string> Keys { get; } = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListSavedKeysAsync(string prefix, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(
            Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList());
}
