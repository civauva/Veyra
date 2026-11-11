using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Veyra.Core.Alerts;
using Veyra.Core.Schema;

namespace Veyra.Alerting.Tests;

public class AlertingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddVeyraAlerting_BindsEvaluationIntervalFromConfiguration()
    {
        using var provider = Provider(new() { ["Veyra:Alerting:EvaluationInterval"] = "00:01:00" });

        var options = provider.GetRequiredService<IOptions<AlertingOptions>>().Value;

        Assert.Equal(TimeSpan.FromMinutes(1), options.EvaluationInterval);
    }

    [Fact]
    public void AddVeyraAlerting_BindsErrorRateRuleFromConfiguration()
    {
        using var provider = Provider(new()
        {
            ["Veyra:Alerting:ErrorRate:Window"] = "00:10:00",
            ["Veyra:Alerting:ErrorRate:MinimumRecords"] = "50",
            ["Veyra:Alerting:ErrorRate:CautionRatio"] = "0.02",
            ["Veyra:Alerting:ErrorRate:WarningRatio"] = "0.1",
        });

        var rule = provider.GetRequiredService<IOptions<AlertingOptions>>().Value.ErrorRate;

        Assert.Equal(TimeSpan.FromMinutes(10), rule.Window);
        Assert.Equal(50, rule.MinimumRecords);
        Assert.Equal(0.02, rule.CautionRatio);
        Assert.Equal(0.1, rule.WarningRatio);
    }

    [Theory]
    [InlineData("Veyra:Alerting:ErrorRate:CautionRatio", "0.2")]
    [InlineData("Veyra:Alerting:ErrorRate:WarningRatio", "1.5")]
    [InlineData("Veyra:Alerting:ErrorRate:CautionRatio", "0")]
    [InlineData("Veyra:Alerting:ErrorRate:Window", "00:00:00")]
    [InlineData("Veyra:Alerting:ErrorRate:MinimumRecords", "0")]
    public void AddVeyraAlerting_InvalidErrorRateRule_FailsValidation(string key, string value)
    {
        using var provider = Provider(new() { [key] = value });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AlertingOptions>>().Value);
    }

    [Fact]
    public void AddVeyraAlerting_BindsNotificationsAndSmtpFromConfiguration()
    {
        using var provider = Provider(new()
        {
            ["Veyra:Alerting:PublicUrl"] = "https://veyra.example.com/monitor",
            ["Veyra:Alerting:Notifications:SendTimeout"] = "00:00:05",
            ["Veyra:Alerting:Notifications:Smtp:Host"] = "smtp.example.com",
            ["Veyra:Alerting:Notifications:Smtp:Port"] = "465",
            ["Veyra:Alerting:Notifications:Smtp:Security"] = "SslOnConnect",
            ["Veyra:Alerting:Notifications:Smtp:From"] = "Veyra <veyra@example.com>",
        });

        var options = provider.GetRequiredService<IOptions<AlertingOptions>>().Value;

        Assert.Equal(new Uri("https://veyra.example.com/monitor/"), options.PublicBaseUri());
        Assert.Equal(TimeSpan.FromSeconds(5), options.Notifications.SendTimeout);
        Assert.Equal(("smtp.example.com", 465), (options.Notifications.Smtp.Host, options.Notifications.Smtp.Port));
        Assert.Equal(Veyra.Alerting.Notifications.SmtpSecurity.SslOnConnect, options.Notifications.Smtp.Security);
        Assert.True(options.Notifications.Smtp.IsConfigured);
    }

    [Fact]
    public void AddVeyraAlerting_NoPublicUrl_HasNoBaseUri()
    {
        using var provider = Provider([]);

        Assert.Null(provider.GetRequiredService<IOptions<AlertingOptions>>().Value.PublicBaseUri());
    }

    [Theory]
    [InlineData("Veyra:Alerting:PublicUrl", "veyra.example.com")]
    [InlineData("Veyra:Alerting:Notifications:SendTimeout", "00:00:00")]
    [InlineData("Veyra:Alerting:Notifications:BatchSize", "0")]
    [InlineData("Veyra:Alerting:Notifications:Smtp:Port", "70000")]
    [InlineData("Veyra:Alerting:Notifications:Smtp:Host", "smtp.example.com")]
    public void AddVeyraAlerting_InvalidNotificationSettings_FailValidation(string key, string value)
    {
        // The last case sets a host without a sender address.
        using var provider = Provider(new() { [key] = value });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AlertingOptions>>().Value);
    }

    [Fact]
    public void AddVeyraAlerting_RegistersAlertStatesAndMigrations()
    {
        var services = new ServiceCollection().AddVeyraAlerting(new ConfigurationBuilder().Build());

        Assert.Contains(services, service => service.ServiceType == typeof(IAlertStates));
        Assert.Contains(
            services,
            service => service.ServiceType == typeof(ISchemaMigrationSource)
                && service.ImplementationType == typeof(AlertingMigrations));
    }

    private static ServiceProvider Provider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new ServiceCollection()
            .AddVeyraAlerting(configuration)
            .BuildServiceProvider();
    }
}
