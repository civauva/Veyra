using System.Text;
using Veyra.Access.SecondFactor;

namespace Veyra.Access.Tests.SecondFactor;

public class Base32Tests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Encode_Rfc4648Vectors_MatchWithoutPadding(string text, string expected)
    {
        Assert.Equal(expected, Base32.Encode(Encoding.ASCII.GetBytes(text)));
    }

    [Theory]
    [InlineData("MZXW6YTBOI", "foobar")]
    [InlineData("mzxw 6ytb oi", "foobar")]
    [InlineData("MZXW6YQ=", "foob")]
    public void Decode_IgnoresCaseSpacesAndPadding(string encoded, string expected)
    {
        Assert.Equal(expected, Encoding.ASCII.GetString(Base32.Decode(encoded)));
    }

    [Fact]
    public void Decode_LetterOutsideTheAlphabet_Throws()
    {
        Assert.Throws<FormatException>(() => Base32.Decode("MZXW1"));
    }
}
