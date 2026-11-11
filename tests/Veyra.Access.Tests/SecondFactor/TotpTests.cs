using System.Text;
using Veyra.Access.SecondFactor;

namespace Veyra.Access.Tests.SecondFactor;

public class TotpTests
{
    // The SHA-1 secret of RFC 6238, appendix B.
    private static readonly byte[] _secret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1111111109, "081804")]
    [InlineData(1111111111, "050471")]
    [InlineData(1234567890, "005924")]
    [InlineData(2000000000, "279037")]
    [InlineData(20000000000, "353130")]
    public void Code_Rfc6238Vectors_MatchTheLastSixDigits(long unixSeconds, string expected)
    {
        var code = Totp.Code(_secret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds)));

        Assert.Equal(expected, code);
    }

    [Fact]
    public void Verify_CodeOfTheCurrentStep_ReturnsTheStep()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);

        var step = Totp.Verify(_secret, "005924", now, lastUsedStep: null);

        Assert.Equal(Totp.StepAt(now), step);
    }

    [Fact]
    public void Verify_CodeOfOneStepEarlier_IsAcceptedForClockDrift()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var previous = Totp.Code(_secret, Totp.StepAt(now) - 1);

        Assert.Equal(Totp.StepAt(now) - 1, Totp.Verify(_secret, previous, now, lastUsedStep: null));
    }

    [Fact]
    public void Verify_CodeOfTwoStepsEarlier_IsRefused()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var old = Totp.Code(_secret, Totp.StepAt(now) - 2);

        Assert.Null(Totp.Verify(_secret, old, now, lastUsedStep: null));
    }

    [Fact]
    public void Verify_CodeOfAStepAlreadyUsed_IsRefused()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);

        Assert.Null(Totp.Verify(_secret, "005924", now, lastUsedStep: Totp.StepAt(now)));
    }

    [Theory]
    [InlineData("005 924")]
    [InlineData(" 005924 ")]
    [InlineData("005-924")]
    public void Verify_CodeWithSpacesOrDash_IsAccepted(string typed)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);

        Assert.NotNull(Totp.Verify(_secret, typed, now, lastUsedStep: null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("abcdef")]
    [InlineData("0059245")]
    public void Verify_NotSixDigits_IsRefused(string typed)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);

        Assert.Null(Totp.Verify(_secret, typed, now, lastUsedStep: null));
    }

    [Fact]
    public void Uri_ForAnAccount_IsTheOtpauthAddressAuthenticatorAppsRead()
    {
        var uri = Totp.Uri(_secret, "lena@example.com");

        Assert.Equal(
            "otpauth://totp/Veyra:lena%40example.com?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&issuer=Veyra"
                + "&algorithm=SHA1&digits=6&period=30",
            uri);
    }

    [Fact]
    public void NewSecret_Is160BitsAndRandom()
    {
        var first = Totp.NewSecret();
        var second = Totp.NewSecret();

        Assert.Equal(20, first.Length);
        Assert.NotEqual(first, second);
    }
}
