using Veyra.Access.Keys;

namespace Veyra.Access.Tests.Keys;

public class IngestionKeyFormatTests
{
    [Fact]
    public void Generate_Always_ReturnsPrefixedUrlSafeKeyOf256Bits()
    {
        var key = IngestionKeyFormat.Generate();

        // "vyk_" plus 32 random bytes in unpadded base64url, which is 43 characters.
        Assert.Matches("^vyk_[A-Za-z0-9_-]{43}$", key);
    }

    [Fact]
    public void Generate_TwoCalls_ReturnDifferentKeys()
    {
        Assert.NotEqual(IngestionKeyFormat.Generate(), IngestionKeyFormat.Generate());
    }

    [Fact]
    public void Hash_SameKey_ReturnsSameHash()
    {
        var key = IngestionKeyFormat.Generate();

        Assert.Equal(IngestionKeyFormat.Hash(key), IngestionKeyFormat.Hash(key));
    }

    [Fact]
    public void Hash_DifferentKeys_ReturnDifferentHashes()
    {
        Assert.NotEqual(
            IngestionKeyFormat.Hash(IngestionKeyFormat.Generate()),
            IngestionKeyFormat.Hash(IngestionKeyFormat.Generate()));
    }

    [Fact]
    public void DisplayPrefix_Key_ReturnsPrefixAndFirstEightRandomCharacters()
    {
        Assert.Equal("vyk_AbCdEfGh", IngestionKeyFormat.DisplayPrefix("vyk_AbCdEfGhIjKlMnOp"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("vyk_")]
    [InlineData("abc_AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_A")]
    [InlineData("vyk_has spaces in it, which base64url never contains!")]
    public void LooksLikeKey_Malformed_ReturnsFalse(string candidate)
    {
        Assert.False(IngestionKeyFormat.LooksLikeKey(candidate));
    }

    [Fact]
    public void LooksLikeKey_GeneratedKey_ReturnsTrue()
    {
        Assert.True(IngestionKeyFormat.LooksLikeKey(IngestionKeyFormat.Generate()));
    }
}
