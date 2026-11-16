using Microsoft.Extensions.Configuration;
using Veyra.Alerting.Settings;
using Veyra.Core.Settings;

namespace Veyra.Alerting.Tests.Settings;

public sealed class AlertingSettingsTests
{
    [Theory]
    [InlineData("Veyra:Alerting:ErrorRate:CautionRatio", "0.2", "Veyra:Alerting:ErrorRate:WarningRatio")]
    [InlineData("Veyra:Alerting:Latency:CautionSeconds", "5", "Veyra:Alerting:Latency:WarningSeconds")]
    [InlineData("Veyra:Alerting:DiskFilling:WarningDays", "20", "Veyra:Alerting:DiskFilling:WarningDays")]
    [InlineData("Veyra:Alerting:DiskSpace:ClearMargin", "0.95", "Veyra:Alerting:DiskSpace:ClearMargin")]
    [InlineData("Veyra:Alerting:Notifications:Smtp:Host", "smtp.example.com", "Veyra:Alerting:Notifications:Smtp:From")]
    public void Validate_BrokenRule_NamesTheFieldToFix(string key, string value, string field)
    {
        var problems = Settings().Validate(Configuration((key, value)));

        Assert.Contains(field, problems.Select(problem => problem.Key));
    }

    [Fact]
    public void Validate_Defaults_HaveNoProblems()
    {
        Assert.Empty(Settings().Validate(Configuration()));
    }

    [Fact]
    public void Definitions_SmtpPassword_IsASecret()
    {
        var password = Settings().Definitions.Single(
            definition => definition.Key.EndsWith(":Smtp:Password", StringComparison.Ordinal));

        Assert.Equal(SettingKind.Secret, password.Kind);
        Assert.Equal("VEYRA_SMTP_PASSWORD", password.EnvironmentVariable);
    }

    [Fact]
    public async Task ReviewAsync_ShorterAlertHistory_IsRisky()
    {
        var note = Assert.Single(await Settings().ReviewAsync(
            [new SettingChange("Veyra:Alerting:HistoryDays", "90", "30")],
            default));

        Assert.True(note.Risky);
        Assert.Contains("older than 30 days", note.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewAsync_RuleSwitchedOff_SaysItOpensNoAlerts()
    {
        var note = Assert.Single(await Settings().ReviewAsync(
            [new SettingChange("Veyra:Alerting:Latency:Enabled", null, "false")],
            default));

        Assert.False(note.Risky);
        Assert.Contains("opens no new alerts", note.Text, StringComparison.Ordinal);
    }

    private static AlertingSettings Settings() => new([new AlertingOptionsRules()]);

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => KeyValuePair.Create(value.Key, (string?)value.Value)))
            .Build();
}
