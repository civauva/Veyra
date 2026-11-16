using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Veyra.Core.Settings;

namespace Veyra.Core.Tests.Settings;

public sealed class SettingChecksTests
{
    [Fact]
    public void Check_CandidateBreaksARule_ReturnsTheProblemForItsKey()
    {
        var candidate = Configuration(("Veyra:Test:CautionDays", "3"), ("Veyra:Test:WarningDays", "14"));

        var problems = SettingChecks.Check("Veyra:Test", candidate, [new TestValidator()]);

        Assert.Equal(
            [new SettingProblem("Veyra:Test:WarningDays", "Veyra:Test:WarningDays must not be above CautionDays.")],
            problems);
    }

    [Fact]
    public void Check_EveryRuleHolds_ReturnsNothing()
    {
        var candidate = Configuration(("Veyra:Test:CautionDays", "14"), ("Veyra:Test:WarningDays", "3"));

        Assert.Empty(SettingChecks.Check("Veyra:Test", candidate, [new TestValidator()]));
    }

    [Fact]
    public void Check_SectionMissing_ChecksTheDefaults()
    {
        Assert.Empty(SettingChecks.Check("Veyra:Test", Configuration(), [new TestValidator()]));
    }

    [Fact]
    public void Check_ValueOfTheWrongType_ReturnsAProblemForItsKey()
    {
        var candidate = Configuration(("Veyra:Test:CautionDays", "soon"));

        var problem = Assert.Single(SettingChecks.Check("Veyra:Test", candidate, [new TestValidator()]));

        Assert.Equal("Veyra:Test:CautionDays", problem.Key);
    }

    [Fact]
    public void Check_MessageNamesNoKey_ReturnsTheProblemWithoutOne()
    {
        var candidate = Configuration(("Veyra:Test:CautionDays", "0"));

        var problem = Assert.Single(SettingChecks.Check("Veyra:Test", candidate, [new TestValidator()]));

        Assert.Null(problem.Key);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => KeyValuePair.Create(value.Key, (string?)value.Value)))
            .Build();

    /// <summary>Options shaped like a rule: a caution level and a warning level that must come sooner.</summary>
    public sealed class TestOptions
    {
        /// <summary>Caution when full within this many days.</summary>
        public int CautionDays { get; set; } = 14;

        /// <summary>Warning when full within this many days.</summary>
        public int WarningDays { get; set; } = 3;
    }

    private sealed class TestValidator : IValidateOptions<TestOptions>
    {
        public ValidateOptionsResult Validate(string? name, TestOptions options)
        {
            if (options.CautionDays <= 0)
            {
                return ValidateOptionsResult.Fail("Days must be positive.");
            }

            return options.WarningDays > options.CautionDays
                ? ValidateOptionsResult.Fail("Veyra:Test:WarningDays must not be above CautionDays.")
                : ValidateOptionsResult.Success;
        }
    }
}
