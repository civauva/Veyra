namespace Veyra.Query.Streams;

/// <summary>
/// One open live stream's place under <c>Veyra:Query:LiveListeners</c>. Disposing it frees the place, once.
/// </summary>
/// <param name="release">Gives the place back to the hub.</param>
internal sealed class LiveLease(Action release) : IDisposable
{
    private int _released;

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            release();
        }
    }
}
