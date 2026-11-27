using Veyra.Query.Search;

namespace Veyra.Query.Tests.Search;

public class SearchRankingTests
{
    [Theory]
    [InlineData("Payments", "payments", "Exact")]
    [InlineData("Payments.API", "pay", "Prefix")]
    [InlineData("old-payments", "PAY", "Contains")]
    [InlineData("orders", "pay", "None")]
    public void RankOf_Name_IsExactThenPrefixThenContains(string name, string text, string expected)
    {
        Assert.Equal(Enum.Parse<SearchRank>(expected), SearchRanking.RankOf(name, text));
    }

    [Fact]
    public void Rank_MixedMatches_PutsExactFirstThenPrefixThenContainsEachByName()
    {
        string[] names = ["zeta-pay", "pay", "Payments.API", "alpha-pay", "payroll", "orders"];

        var ranked = SearchRanking.Rank(names, name => name, "pay").ToList();

        Assert.Equal(["pay", "Payments.API", "payroll", "alpha-pay", "zeta-pay"], ranked);
    }

    [Fact]
    public void Rank_SameNameInOtherCase_OrdersByNameIgnoringCaseThenOrdinal()
    {
        string[] names = ["payB", "PayA", "paya"];

        var ranked = SearchRanking.Rank(names, name => name, "pay").ToList();

        Assert.Equal(["PayA", "paya", "payB"], ranked);
    }
}
