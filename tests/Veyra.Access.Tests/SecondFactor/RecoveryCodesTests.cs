using System.Text.RegularExpressions;
using Veyra.Access.SecondFactor;

namespace Veyra.Access.Tests.SecondFactor;

public partial class RecoveryCodesTests
{
    [Fact]
    public void Generate_TenCodes_AreDistinctAndReadable()
    {
        var codes = RecoveryCodes.Generate();

        Assert.Equal(10, codes.Count);
        Assert.Equal(10, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Matches(CodeShape(), code));
    }

    [Theory]
    [InlineData("K7QF-29XM")]
    [InlineData("k7qf29xm")]
    [InlineData(" K7QF 29XM ")]
    public void Hash_SameCodeTypedDifferently_IsTheSameHash(string typed)
    {
        Assert.Equal(RecoveryCodes.Hash("K7QF-29XM"), RecoveryCodes.Hash(typed));
    }

    [Fact]
    public void Hash_DifferentCodes_Differ()
    {
        Assert.NotEqual(RecoveryCodes.Hash("K7QF-29XM"), RecoveryCodes.Hash("K7QF-29XN"));
    }

    [Theory]
    [InlineData("K7QF-29XM", true)]
    [InlineData("k7qf 29xm", true)]
    [InlineData("482093", false)]
    [InlineData("", false)]
    public void LooksLikeOne_TellsRecoveryCodesFromAppCodes(string typed, bool expected)
    {
        Assert.Equal(expected, RecoveryCodes.LooksLikeOne(typed));
    }

    // Four and four characters from the alphabet without 0, O, 1, and I, which look alike.
    [GeneratedRegex("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$")]
    private static partial Regex CodeShape();
}
