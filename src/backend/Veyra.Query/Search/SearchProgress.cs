namespace Veyra.Query.Search;

/// <summary>
/// The best answer a search group has so far. The group replaces it as it learns more, and the search reads it when
/// the group's time is up, so a group that runs out still answers with what it found.
/// </summary>
/// <typeparam name="T">The answer: a list of items, or a count.</typeparam>
/// <param name="initial">The answer before anything is found.</param>
internal sealed class SearchProgress<T>(T initial)
{
    private readonly Lock _lock = new();
    private T _value = initial;

    /// <summary>The answer so far.</summary>
    public T Value
    {
        get
        {
            lock (_lock)
            {
                return _value;
            }
        }

        set
        {
            lock (_lock)
            {
                _value = value;
            }
        }
    }
}
