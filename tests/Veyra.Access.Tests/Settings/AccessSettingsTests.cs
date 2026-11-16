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
