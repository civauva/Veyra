using Microsoft.Extensions.Configuration;
using Veyra.Alerting.Alerts;
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

    [Theory]
    [InlineData("Veyra:Alerting:Runbooks:error-rate", "ftp://wiki.example.com/error-rate")]
    [InlineData("Veyra:Alerting:Runbooks:db-head-blocker", "wiki/head-blocker")]
    [InlineData("Veyra:Alerting:Runbooks:planet", "https://wiki.example.com/planet")]
    [InlineData("Veyra:Alerting:Incidents:GroupingMinutes", "0")]
    public void Validate_BrokenIncidentSetting_NamesItsKey(string key, string value)
    {
        var problems = Settings().Validate(Configuration((key, value)));

        Assert.Contains(key, problems.Select(problem => problem.Key));
    }

    [Fact]
    public void Validate_RunbookLinksOverHttpOrHttps_HaveNoProblems()
    {
        var problems = Settings().Validate(Configuration(
            ("Veyra:Alerting:Runbooks:error-rate", "https://wiki.example.com/runbooks/error-rate"),
            ("Veyra:Alerting:Runbooks:db-head-blocker", "http://wiki.local/head-blocker"),
            ("Veyra:Alerting:Runbooks:disk-space", "")));

        Assert.Empty(problems);
    }

    [Fact]
    public void Definitions_Runbooks_OneOptionalLinkPerRuleUnderAlertRules()
    {
        var runbooks = Settings().Definitions.Where(definition => definition.Group == "Runbooks").ToList();

        Assert.Equal(
            AlertRules.All.Select(rule => $"Veyra:Alerting:Runbooks:{rule}"),
            runbooks.Select(definition => definition.Key));
        Assert.All(runbooks, definition =>
        {
            Assert.Equal((SettingSection.Rules, SettingKind.Text, true), (definition.Section, definition.Kind, definition.Optional));
            Assert.Null(definition.Default);
        });
    }

    [Fact]
    public void Definitions_GroupingWindow_IsFifteenMinutesUnderIncidents()
    {
        var grouping = Settings().Definitions.Single(definition => definition.Key == "Veyra:Alerting:Incidents:GroupingMinutes");

        Assert.Equal(("Incidents", SettingSection.Rules, SettingKind.WholeNumber, "15"), (grouping.Group, grouping.Section, grouping.Kind, grouping.Default));
    }

    private static AlertingSettings Settings() => new([new AlertingOptionsRules()]);

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => KeyValuePair.Create(value.Key, (string?)value.Value)))
            .Build();
}
