using Veyra.Core.Telemetry;

namespace Veyra.Core.Tests.Telemetry;

public class LogCursorTests
{
    [Fact]
    public void TryDecode_EncodedCursor_ReturnsTheSameCursor()
    {
        var cursor = new LogCursor(new DateTimeOffset(2026, 11, 9, 12, 0, 0, 123, TimeSpan.Zero), 42);

        Assert.True(LogCursor.TryDecode(cursor.Encode(), out var decoded));
        Assert.Equal(cursor, decoded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 at all!")]
    [InlineData("MTIzNDU")]
    [InlineData("LTE6Mg")]
    public void TryDecode_InvalidToken_ReturnsFalse(string token)
    {
        Assert.False(LogCursor.TryDecode(token, out _));
    }
}
