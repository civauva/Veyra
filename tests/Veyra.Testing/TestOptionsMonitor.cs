using Microsoft.Extensions.Options;

namespace Veyra.Testing;

/// <summary>
/// Options a test can change while the code under test runs, the way a save on the Settings screen changes them
/// (ADR 0017).
/// </summary>
/// <typeparam name="T">The options type.</typeparam>
/// <param name="value">The options to start with.</param>
public sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    private readonly List<Action<T, string?>> _listeners = [];

    /// <inheritdoc />
    public T CurrentValue { get; private set; } = value;

    /// <inheritdoc />
    public T Get(string? name) => CurrentValue;

    /// <inheritdoc />
    public IDisposable? OnChange(Action<T, string?> listener)
    {
        _listeners.Add(listener);

        return null;
    }

    /// <summary>Changes the options and tells every listener, as a reload of configuration does.</summary>
    /// <param name="options">The new options.</param>
    public void Set(T options)
    {
        CurrentValue = options;

        foreach (var listener in _listeners)
        {
            listener(options, Options.DefaultName);
        }
    }
}
