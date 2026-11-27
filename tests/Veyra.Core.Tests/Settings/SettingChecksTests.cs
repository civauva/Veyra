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

    [Fact]
    public void Check_KeyNamesARuleWithHyphens_ReturnsTheProblemForTheWholeKey()
    {
        var candidate = Configuration(("Veyra:Test:Links:error-rate", "ftp://example.com"));

        var problem = Assert.Single(SettingChecks.Check("Veyra:Test", candidate, [new LinkValidator()]));

        Assert.Equal("Veyra:Test:Links:error-rate", problem.Key);
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

    /// <summary>Options keyed by rule names, which hold hyphens, as runbook links are (ADR 0026).</summary>
    public sealed class LinkOptions
    {
        /// <summary>A link per rule.</summary>
        public Dictionary<string, string> Links { get; set; } = [];
    }

    private sealed class LinkValidator : IValidateOptions<LinkOptions>
    {
        public ValidateOptionsResult Validate(string? name, LinkOptions options)
        {
            var wrong = options.Links
                .Where(link => !link.Value.StartsWith("http", StringComparison.Ordinal))
                .Select(link => $"Veyra:Test:Links:{link.Key} must be an http or https address.")
                .ToList();

            return wrong.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(wrong);
        }
    }
}
