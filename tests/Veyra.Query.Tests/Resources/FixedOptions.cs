using Microsoft.Extensions.Options;

namespace Veyra.Query.Tests.Resources;

/// <summary>Options that stay as a test sets them.</summary>
internal sealed class FixedOptions<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
