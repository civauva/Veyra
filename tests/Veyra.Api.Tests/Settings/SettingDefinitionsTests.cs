using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Veyra.Access;
using Veyra.Agents;
using Veyra.Alerting;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.Catalog;
using Veyra.Core;
using Veyra.Core.Settings;
using Veyra.Ingestion;
using Veyra.Query;
using Veyra.Storage;

namespace Veyra.Api.Tests.Settings;

/// <summary>
/// No setting may stay environment-only by accident (ADR 0017): every bound options property has a definition on the
/// Settings screen, with the default the code really uses.
/// </summary>
public sealed class SettingDefinitionsTests
{
    // Needed before the database can be read, or a way back in when nobody can sign in. They stay in deploy/.env.
    // The built-in agent's folder and address are the Compose file's wiring between two containers (ADR 0019).
    private static readonly string[] _outside =
    [
        "Veyra:Storage:ConnectionString",
        "Veyra:Access:RemoveSecondFactor",
        "Veyra:Agents:BuiltInDirectory",
        "Veyra:Agents:BuiltInServerUrl",
    ];

    private static readonly (string Section, Type Options)[] _modules =
    [
        (VeyraOptionNames.Access, typeof(AccessOptions)),
        (VeyraOptionNames.Agents, typeof(AgentsOptions)),
        (VeyraOptionNames.Alerting, typeof(AlertingOptions)),
        (VeyraOptionNames.Ingestion, typeof(IngestionOptions)),
        (VeyraOptionNames.Query, typeof(QueryOptions)),
        (VeyraOptionNames.Storage, typeof(StorageOptions)),
    ];

    [Fact]
    public void Definitions_EveryBoundOption_HasExactlyOneDefinition()
    {
        var expected = BoundOptions()
            .Select(option => option.Key)
            .Concat(CatalogKeys())
            .Concat(RunbookKeys())
            .Except(_outside)
            .Order()
            .ToList();

        var defined = Definitions().Select(definition => definition.Key).Order().ToList();

        Assert.Equal(expected, defined);
    }

    [Fact]
    public void Definitions_Default_IsTheValueInCode()
    {
        var inCode = BoundOptions().ToDictionary(option => option.Key, option => option.Default);

        var wrong = Definitions()
            .Where(definition => inCode.ContainsKey(definition.Key) && inCode[definition.Key] != definition.Default)
            .Select(definition => $"{definition.Key}: defined {definition.Default}, code {inCode[definition.Key]}")
            .ToList();

        Assert.Empty(wrong);
    }

    [Fact]
    public void Validate_Defaults_EveryModuleAcceptsThem()
    {
        var empty = new ConfigurationBuilder().Build();

        var problems = ModuleSettings().SelectMany(module => module.Validate(empty)).ToList();

        Assert.Empty(problems);
    }

    [Fact]
    public void Definitions_SimpleView_ShowsTheBasicsPeopleChangeOften()
    {
        string[] simple =
        [
            "Veyra:Storage:LogRetentionDays",
            "Veyra:Storage:MaxLogStorageGigabytes",
            "Veyra:Storage:MetricRetentionDays",
            "Veyra:Storage:DatabaseSessionRetentionDays",
            "Veyra:Storage:TraceRetentionDays",
            "Veyra:Storage:MaxTraceStorageGigabytes",
            "Veyra:Alerting:ErrorRate:Enabled",
            "Veyra:Alerting:ErrorRate:CautionRatio",
            "Veyra:Alerting:ErrorRate:WarningRatio",
            "Veyra:Alerting:Latency:Enabled",
            "Veyra:Alerting:Latency:CautionSeconds",
            "Veyra:Alerting:Latency:WarningSeconds",
            "Veyra:Alerting:DiskSpace:Enabled",
            "Veyra:Alerting:DiskSpace:CautionRatio",
            "Veyra:Alerting:DiskSpace:WarningRatio",
            "Veyra:Alerting:DiskFilling:Enabled",
            "Veyra:Alerting:DiskFilling:CautionDays",
            "Veyra:Alerting:DiskFilling:WarningDays",
            "Veyra:Alerting:ContainerRestarts:Enabled",
            "Veyra:Alerting:ContainerRestarts:CautionRestarts",
            "Veyra:Alerting:ContainerRestarts:WarningRestarts",
            "Veyra:Alerting:ContainerMemory:Enabled",
            "Veyra:Alerting:ContainerMemory:CautionRatio",
            "Veyra:Alerting:ContainerMemory:WarningRatio",
            "Veyra:Alerting:DatabaseBlocking:Enabled",
            "Veyra:Alerting:DatabaseBlocking:CautionSessions",
            "Veyra:Alerting:DatabaseBlocking:WarningSessions",
            "Veyra:Alerting:DatabaseDeadlocks:Enabled",
            "Veyra:Alerting:DatabaseDeadlocks:CautionDeadlocks",
            "Veyra:Alerting:DatabaseDeadlocks:WarningDeadlocks",
            "Veyra:Alerting:DatabaseLongQuery:Enabled",
            "Veyra:Alerting:DatabaseLongQuery:CautionMinutes",
            "Veyra:Alerting:DatabaseLongQuery:WarningMinutes",
            "Veyra:Alerting:DatabaseBackupAge:Enabled",
            "Veyra:Alerting:DatabaseBackupAge:CautionHours",
            "Veyra:Alerting:DatabaseBackupAge:WarningHours",
            "Veyra:Alerting:DatabaseAgentJob:Enabled",
            "Veyra:Alerting:DatabaseConnections:Enabled",
            "Veyra:Alerting:DatabaseConnections:CautionRatio",
            "Veyra:Alerting:DatabaseConnections:WarningRatio",
            "Veyra:Alerting:DatabaseWraparound:Enabled",
            "Veyra:Alerting:DatabaseWraparound:CautionRatio",
            "Veyra:Alerting:DatabaseWraparound:WarningRatio",
            "Veyra:Alerting:Notifications:Smtp:Host",
            "Veyra:Alerting:Notifications:Smtp:Port",
            "Veyra:Alerting:Notifications:Smtp:Security",
            "Veyra:Alerting:Notifications:Smtp:From",
            "Veyra:Alerting:Notifications:Smtp:Username",
            "Veyra:Alerting:Notifications:Smtp:Password",
            "Veyra:Alerting:PublicUrl",
            "Veyra:Access:SessionIdleTimeout",
            "Veyra:Access:MinimumPasswordLength",
            "Veyra:Access:LockoutThreshold",
            "Veyra:Access:LockoutDuration",
            "Veyra:Access:SignInHistoryDays",
            "Veyra:Access:DefaultLook",
            "Veyra:Access:PeopleChooseLook",
        ];

        var shown = Definitions().Where(definition => !definition.Advanced).Select(definition => definition.Key);

        Assert.Equal(simple.Concat(CatalogKeys()).Order(), shown.Order());
    }

    [Fact]
    public void Definitions_AdvancedSection_HoldsOnlyAdvancedSettings()
    {
        Assert.All(
            Definitions().Where(definition => definition.Section is SettingSection.Advanced or SettingSection.Ingestion),
            definition => Assert.True(definition.Advanced, definition.Key));
    }

    [Fact]
    public void Definitions_Choice_ListsItsChoices_AndNothingElseDoes()
    {
        Assert.All(Definitions(), definition =>
            Assert.Equal(definition.Kind == SettingKind.Choice, definition.Choices.Count > 0));
    }

    private static List<SettingDefinition> Definitions() =>
        ModuleSettings().SelectMany(module => module.Definitions).ToList();

    private static List<IModuleSettings> ModuleSettings()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = Environments.Production });
        services.AddLogging();
        services
            .AddVeyraIngestion(configuration)
            .AddVeyraStorage(configuration)
            .AddVeyraQuery(configuration)
            .AddVeyraAlerting(configuration)
            .AddVeyraAccess(configuration)
            .AddVeyraAgents(configuration);

        return services.BuildServiceProvider().GetServices<IModuleSettings>().ToList();
    }

    // Runbook links bind into a dictionary by rule, so reflection cannot see them: every rule has one (ADR 0026).
    private static IEnumerable<string> RunbookKeys() =>
        AlertRules.All.Select(rule => $"Veyra:Alerting:Runbooks:{rule}");

    // The catalog rules bind into a dictionary, so reflection cannot see them: each has a switch, and its levels
    // unless it is a flag (ADR 0020). Their defaults are the catalog's, checked by the catalog's own tests.
    private static IEnumerable<string> CatalogKeys() =>
        RuleCatalog.All.SelectMany(rule =>
        {
            var section = $"Veyra:Alerting:{RuleCatalog.Section}:{rule.Setting}";
            var levels = rule.Unit == MeasureUnit.Flag
                ? []
                : rule.DefaultCaution is null
                    ? new[] { $"{section}:Warning" }
                    : new[] { $"{section}:Caution", $"{section}:Warning" };

            return levels.Prepend($"{section}:Enabled");
        });

    // Every leaf property an options class binds, with its key and the default a fresh instance has.
    private static IEnumerable<(string Key, string? Default)> BoundOptions() =>
        _modules.SelectMany(module => Leaves(module.Section, module.Options, Activator.CreateInstance(module.Options)));

    private static IEnumerable<(string Key, string? Default)> Leaves(string prefix, Type type, object? instance)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if ((!property.CanWrite && !IsNested(property.PropertyType)) || IsDictionary(property.PropertyType))
            {
                continue;
            }

            var key = $"{prefix}:{property.Name}";
            var value = instance is null ? null : property.GetValue(instance);

            if (IsNested(property.PropertyType))
            {
                foreach (var leaf in Leaves(key, property.PropertyType, value))
                {
                    yield return leaf;
                }

                continue;
            }

            yield return (key, Format(value));
        }
    }

    private static bool IsDictionary(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>);

    private static bool IsNested(Type type) =>
        type.IsClass && type != typeof(string) && type.Namespace?.StartsWith("Veyra", StringComparison.Ordinal) == true;

    // As configuration would write the value, so it can be compared with a definition's default.
    private static string? Format(object? value) => value switch
    {
        null => null,
        bool flag => flag ? "true" : "false",
        TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
