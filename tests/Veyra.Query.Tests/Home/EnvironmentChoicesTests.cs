using Veyra.Query.Home;

namespace Veyra.Query.Tests.Home;

/// <summary>The server lists the environments the switcher's find box keeps (VEY-67).</summary>
public class EnvironmentChoicesTests
{
    private static readonly HomeEnvironmentResponse[] _seen =
    [
        new("production", HomeConditionKind.Storm, 0.058),
        new("staging", HomeConditionKind.Showers, 0.01),
        new("stage-eu", HomeConditionKind.Clear, null),
    ];

    [Fact]
    public void Build_NoSearch_ListsAllWithTheWorstWeatherAndEveryEnvironment()
    {
        var choices = EnvironmentChoices.Build(_seen, null);

        Assert.Equal(HomeConditionKind.Storm, choices.All!.Condition);
        Assert.Equal(["production", "staging", "stage-eu"], choices.Environments.Select(one => one.Name));
    }

    [Fact]
    public void Build_Search_KeepsTheNamesThatHoldItInAnyCase()
    {
        var choices = EnvironmentChoices.Build(_seen, " STAG ");

        Assert.Null(choices.All);
        Assert.Equal(["staging", "stage-eu"], choices.Environments.Select(one => one.Name));
    }

    [Fact]
    public void Build_SearchForAll_KeepsTheChoiceOfEveryEnvironment()
    {
        var choices = EnvironmentChoices.Build(_seen, "all");

        Assert.NotNull(choices.All);
        Assert.Empty(choices.Environments);
    }

    [Fact]
    public void Build_NoEnvironments_AllIsClear()
    {
        var choices = EnvironmentChoices.Build([], null);

        Assert.Equal(HomeConditionKind.Clear, choices.All!.Condition);
    }
}
