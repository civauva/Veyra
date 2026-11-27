using Microsoft.Extensions.Configuration;
using Veyra.Access.Settings;
using Veyra.Core.Settings;

namespace Veyra.Access.Tests.Settings;

public sealed class AccessSettingsTests
{
    [Fact]
    public void Validate_LocalSignInOff_IsRefusedWhileThereIsNoOtherWayToSignIn()
    {
        var problem = Assert.Single(Settings().Validate(Configuration(("Veyra:Access:LocalSignInEnabled", "false"))));

        Assert.Equal("Veyra:Access:LocalSignInEnabled", problem.Key);
    }

    [Theory]
    [InlineData("Veyra:Access:MinimumPasswordLength", "8")]
    [InlineData("Veyra:Access:LockoutThreshold", "0")]
    [InlineData("Veyra:Access:SessionIdleTimeout", "00:00:00")]
    [InlineData("Veyra:Access:LockoutDuration", "-00:01:00")]
    public void Validate_BrokenRule_NamesTheField(string key, string value)
    {
        Assert.Contains(key, Settings().Validate(Configuration((key, value))).Select(problem => problem.Key));
    }

    [Fact]
    public async Task ReviewAsync_ShorterSignInHistory_IsRisky()
    {
        var note = Assert.Single(await Settings().ReviewAsync(
            [new SettingChange("Veyra:Access:SignInHistoryDays", null, "30")],
            default));

        Assert.True(note.Risky);
        Assert.Contains("older than 30 days", note.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewAsync_LongerMinimumPassword_SaysExistingPasswordsKeepWorking()
    {
        var note = Assert.Single(await Settings().ReviewAsync(
            [new SettingChange("Veyra:Access:MinimumPasswordLength", "12", "16")],
            default));

        Assert.False(note.Risky);
        Assert.Contains("Existing passwords keep working", note.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_UnknownDefaultLook_NamesTheField()
    {
        var problem = Assert.Single(Settings().Validate(Configuration(("Veyra:Access:DefaultLook", "city"))));

        Assert.Equal("Veyra:Access:DefaultLook", problem.Key);
    }

    [Theory]
    [InlineData("forecast")]
    [InlineData("console")]
    public void Validate_KnownDefaultLook_HasNoProblem(string look)
    {
        Assert.Empty(Settings().Validate(Configuration(("Veyra:Access:DefaultLook", look))));
    }

    [Fact]
    public void Definitions_Look_AreInTheAppearanceSection_WithTheLooksAsChoices()
    {
        var looks = Settings().Definitions
            .Where(definition => definition.Section == SettingSection.Appearance)
            .ToList();

        Assert.Equal(["Veyra:Access:DefaultLook", "Veyra:Access:PeopleChooseLook"], looks.Select(look => look.Key));
        Assert.Equal(
            (SettingKind.Choice, "console", "Look", "Default look"),
            (looks[0].Kind, looks[0].Default, looks[0].Group, looks[0].Label));
        Assert.Equal(["forecast", "console"], looks[0].Choices);
        Assert.Equal(
            (SettingKind.Switch, "true", "Look", "Let people choose their look"),
            (looks[1].Kind, looks[1].Default, looks[1].Group, looks[1].Label));
        Assert.All(looks, look => Assert.False(look.Advanced));
    }

    [Fact]
    public void Definitions_DoNotIncludeTheRecoverySwitch()
    {
        Assert.DoesNotContain(
            Settings().Definitions,
            definition => definition.Key == "Veyra:Access:RemoveSecondFactor");
    }

    private static AccessSettings Settings() => new([new AccessOptionsRules()]);

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => KeyValuePair.Create(value.Key, (string?)value.Value)))
            .Build();
}
