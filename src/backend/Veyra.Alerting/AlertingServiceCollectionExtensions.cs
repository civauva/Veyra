using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.Api;
using Veyra.Alerting.ErrorRates;
using Veyra.Alerting.MetricRules;
using Veyra.Alerting.Notifications;
using Veyra.Core;
using Veyra.Core.Alerts;
using Veyra.Core.Schema;

namespace Veyra.Alerting;

/// <summary>
/// Registers the Alerting module with the dependency injection container. <c>Veyra.Api</c> calls it at startup.
/// </summary>
public static class AlertingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Alerting module: rule evaluation, the alerts it raises, and their notifications.
    /// </summary>
    /// <remarks>
    /// The module reads log rates through <see cref="Veyra.Core.Telemetry.ILogRates"/>, which the Storage module
    /// registers, and disk and latency figures through <see cref="Veyra.Core.Metrics.Health.IMetricHealth"/>, which
    /// the Query module registers. It offers its alerts to other modules as <see cref="IAlertStates"/>.
    /// </remarks>
    /// <param name="services">The service collection to add the module to.</param>
    /// <param name="configuration">
    /// The application configuration. The module reads its <c>Veyra:Alerting</c> section.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddVeyraAlerting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AlertingOptions>()
            .Bind(configuration.GetSection(VeyraOptionNames.Alerting))
            .Validate(o => o.EvaluationInterval > TimeSpan.Zero, "Veyra:Alerting:EvaluationInterval must be positive.")
            .Validate(o => o.ErrorRate.Window > TimeSpan.Zero, "Veyra:Alerting:ErrorRate:Window must be positive.")
            .Validate(
                o => o.ErrorRate.MinimumRecords >= 1,
                "Veyra:Alerting:ErrorRate:MinimumRecords must be at least 1.")
            .Validate(
                o => o.ErrorRate.CautionRatio > 0
                    && o.ErrorRate.CautionRatio <= o.ErrorRate.WarningRatio
                    && o.ErrorRate.WarningRatio <= 1,
                "Veyra:Alerting:ErrorRate needs 0 < CautionRatio <= WarningRatio <= 1.")
            .Validate(o => o.HistoryDays >= 1, "Veyra:Alerting:HistoryDays must be at least 1.")
            .Validate(
                o => o.DiskSpace.CautionRatio > 0
                    && o.DiskSpace.CautionRatio <= o.DiskSpace.WarningRatio
                    && o.DiskSpace.WarningRatio <= 1
                    && o.DiskSpace.ClearMargin >= 0
                    && o.DiskSpace.ClearMargin < o.DiskSpace.CautionRatio,
                "Veyra:Alerting:DiskSpace needs 0 < CautionRatio <= WarningRatio <= 1 "
                + "and 0 <= ClearMargin < CautionRatio.")
            .Validate(
                o => o.DiskFilling.WarningDays > 0
                    && o.DiskFilling.WarningDays <= o.DiskFilling.CautionDays
                    && o.DiskFilling.ClearFactor >= 1,
                "Veyra:Alerting:DiskFilling needs 0 < WarningDays <= CautionDays and a ClearFactor of 1 or more.")
            .Validate(
                o => o.Latency.CautionSeconds > 0
                    && o.Latency.CautionSeconds <= o.Latency.WarningSeconds
                    && o.Latency.ClearFactor is > 0 and <= 1
                    && o.Latency.MinimumRequests >= 1,
                "Veyra:Alerting:Latency needs 0 < CautionSeconds <= WarningSeconds, 0 < ClearFactor <= 1, "
                + "and a MinimumRequests of 1+.")
            .Validate(
                o => string.IsNullOrWhiteSpace(o.PublicUrl)
                    || (Uri.TryCreate(o.PublicUrl, UriKind.Absolute, out var url)
                        && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)),
                "Veyra:Alerting:PublicUrl must be a full http or https address, for example https://veyra.example.com.")
            .Validate(
                o => o.Notifications.DispatchInterval > TimeSpan.Zero
                    && o.Notifications.SendTimeout > TimeSpan.Zero
                    && o.Notifications.BatchSize >= 1,
                "Veyra:Alerting:Notifications needs a positive DispatchInterval and SendTimeout and a BatchSize of 1+.")
            .Validate(
                o => o.Notifications.Smtp.Port is >= 1 and <= 65535,
                "Veyra:Alerting:Notifications:Smtp:Port must be between 1 and 65535.")
            .Validate(
                o => !o.Notifications.Smtp.IsConfigured
                    || MimeKit.MailboxAddress.TryParse(o.Notifications.Smtp.From, out _),
                "Set Veyra:Alerting:Notifications:Smtp:From (VEYRA_SMTP_FROM) to the sender's email address.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISchemaMigrationSource, AlertingMigrations>();
        services.AddSingleton<AlertStore>();
        services.AddSingleton<IAlertStates>(provider => provider.GetRequiredService<AlertStore>());
        services.AddSingleton<AlertTransitions>();
        services.AddSingleton<ErrorRateEvaluator>();
        services.AddSingleton<MetricRuleEvaluator>();
        services.AddSingleton<AlertRuleRun>();
        services.AddSingleton<AlertDetailsBuilder>();
        services.AddHostedService<AlertEvaluation>();

        AddNotifications(services);
        AddControllers(services);

        return services;
    }

    private static void AddNotifications(IServiceCollection services)
    {
        // Webhook URLs are encrypted with the same key ring as session cookies. Access names the application; adding
        // Data Protection here as well keeps this module working on its own, and a second call changes nothing.
        services.AddDataProtection();

        // No redirects: a webhook that moved should be updated, and following one could send the message, and the
        // token in it, somewhere nobody chose.
        services.AddHttpClient(WebhookSender.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton<ChannelStore>();
        services.AddSingleton<DeliveryStore>();
        services.AddSingleton<WebhookUrlProtector>();
        services.AddSingleton<WebhookSender>();
        services.AddSingleton<EmailSender>();
        services.AddSingleton<ChannelSender>();
        services.AddSingleton<ChannelService>();
        services.AddSingleton<NotificationDispatcher>();
        services.AddHostedService<NotificationDispatch>();
    }

    // The controllers are public, as MVC requires, but the services they use stay internal to this module. So the
    // module builds its controllers itself. The host's AddControllersAsServices() only adds controllers that are not
    // registered yet.
    private static void AddControllers(IServiceCollection services)
    {
        services.AddTransient(provider => new AlertsController(
            provider.GetRequiredService<AlertDetailsBuilder>(),
            provider.GetRequiredService<AlertStore>(),
            provider.GetRequiredService<IOptions<AlertingOptions>>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddTransient(provider => new NotificationChannelsController(
            provider.GetRequiredService<ChannelService>(),
            provider.GetRequiredService<IOptions<AlertingOptions>>()));
    }
}
