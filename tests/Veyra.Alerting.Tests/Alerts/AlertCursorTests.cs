using Veyra.Alerting.Alerts;

namespace Veyra.Alerting.Tests.Alerts;

public class AlertCursorTests
{
    [Fact]
    public void Encode_ThenTryDecode_ReturnsTheSameCursor()
    {
        var cursor = new AlertCursor(new DateTimeOffset(2026, 11, 10, 7, 34, 12, TimeSpan.Zero), Guid.CreateVersion7());

        Assert.True(AlertCursor.TryDecode(cursor.Encode(), out var decoded));
        Assert.Equal(cursor, decoded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("MTIzNDU")]
    [InlineData("MTIzOm5vdC1hLWd1aWQ")]
    public void TryDecode_Garbage_ReturnsFalse(string token)
    {
        Assert.False(AlertCursor.TryDecode(token, out var cursor));
        Assert.Null(cursor);
    }
}
