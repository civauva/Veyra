using Veyra.Query.Search;

namespace Veyra.Query.Tests.Search;

public class SearchChunksTests
{
    private static readonly DateTimeOffset _to = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewestFirst_Day_CoversTheWindowNewestFirstInDoublingParts()
    {
        var chunks = SearchChunks.NewestFirst(_to.AddDays(-1), _to).ToList();

        Assert.Equal(
            [1, 1, 2, 4, 8, 8],
            chunks.Select(chunk => (chunk.To - chunk.From).TotalHours));
        Assert.Equal(_to, chunks[0].To);
        Assert.Equal(_to.AddDays(-1), chunks[^1].From);
        Assert.All(chunks.Zip(chunks.Skip(1)), pair => Assert.Equal(pair.First.From, pair.Second.To));
    }

    [Fact]
    public void NewestFirst_ShortWindow_IsOnePart()
    {
        var chunks = SearchChunks.NewestFirst(_to.AddMinutes(-15), _to).ToList();

        Assert.Equal([(_to.AddMinutes(-15), _to)], chunks);
    }
}
