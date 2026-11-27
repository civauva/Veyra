using System.Globalization;
using Veyra.Agents.SqlServers;

namespace Veyra.Agents.Integrations;

/// <summary>
/// The modules the SQL Server template's reads are grouped into (ADR 0028), which an Admin switches and times per
/// server. Each server stores only what differs from the defaults, so a server stored before modules existed reads
/// exactly as it did.
/// </summary>
internal static class SqlServerModules
{
    /// <summary>The ID of the module that cannot be switched off.</summary>
    public const string Core = "core";

    // The intervals a module may be given, each module taking those within its own range.
    private static readonly int[] _ladder = [10, 15, 30, 60, 300, 900, 3600];

    /// <summary>Every module, in the order the wizard lists them.</summary>
    public static IReadOnlyList<SqlServerModule> All { get; } =
    [
        Module(
            Core,
            "Core",
            "Performance and health: requests, waits, memory, connections, each statement's time, and the server.",
            defaultSeconds: 15,
            min: 10,
            max: 60,
            required: true,
            "sqlserver",
            "sqlquery",
            "sqlquery/server",
            "sqlquery/azure"),
        Module(
            "activity",
            "Activity and blocking",
            "The busy sessions, blocking chains, and open transactions, for the live view.",
            defaultSeconds: 10,
            min: 10,
            max: 60,
            required: false,
            "sqlquery/live"),
        Module(
            "queries",
            "Queries",
            "Execution plans of the heaviest statements, and Query Store's history.",
            defaultSeconds: 900,
            min: 300,
            max: 3600,
            required: false,
            "sqlquery/plans",
            "sqlquery/querystore"),
        Module(
            "storage",
            "Storage",
            "Databases, files, volumes, the log, and integrity. The login's permissions are read even when it is off.",
            defaultSeconds: 300,
            min: 60,
            max: 3600,
            required: false,
            "sqlquery/storage",
            "sqlquery/azurestorage"),
        Module(
            "backups",
            "Backups",
            "Every backup msdb records.",
            defaultSeconds: 300,
            min: 60,
            max: 3600,
            required: false,
            "sqlquery/backups"),
        Module(
            "jobs",
            "Jobs",
            "SQL Agent jobs, their runs, and their steps.",
            defaultSeconds: 60,
            min: 30,
            max: 900,
            required: false,
            "sqlquery/jobs"),
        Module(
            "events",
            "Events",
            "Deadlocks and errors from the system_health session.",
            defaultSeconds: 60,
            min: 30,
            max: 900,
            required: false,
            "sqlquery/events",
            "sqlquery/azureevents"),
        Module(
            "configuration",
            "Configuration",
            "The server's and the databases' settings, and when they change.",
            defaultSeconds: 3600,
            min: 900,
            max: 3600,
            required: false,
            "sqlquery/config"),
        Module(
            "indexes",
            "Indexes",
            "Index usage, missing indexes, and fragmentation.",
            defaultSeconds: 3600,
            min: 900,
            max: 3600,
            required: false,
            "sqlquery/indexes"),
    ];

    private static readonly Dictionary<string, SqlServerModule> _byReceiver = All
        .SelectMany(module => module.Receivers.Select(receiver => (Receiver: receiver, Module: module)))
        .ToDictionary(pair => pair.Receiver, pair => pair.Module, StringComparer.Ordinal);

    /// <summary>Finds a module by its ID.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The module, or <see langword="null"/> when there is none.</returns>
    public static SqlServerModule? Find(string? id) =>
        All.FirstOrDefault(module => string.Equals(module.Id, id, StringComparison.Ordinal));

    /// <summary>The module a template receiver belongs to.</summary>
    /// <param name="receiver">The receiver's name in the template, such as <c>sqlquery/jobs</c>.</param>
    /// <returns>
    /// Its module; Core for a receiver no module names, so a receiver added to the template is read until it is
    /// given one.
    /// </returns>
    public static SqlServerModule ModuleOf(string receiver) => _byReceiver.GetValueOrDefault(receiver) ?? All[0];

    /// <summary>Whether a module names a template receiver.</summary>
    /// <param name="receiver">The receiver's name in the template.</param>
    /// <returns>True when a module names it.</returns>
    public static bool Names(string receiver) => _byReceiver.ContainsKey(receiver);

    /// <summary>Every module at its default: on, at its default interval.</summary>
    /// <returns>The choices, in the catalog's order.</returns>
    public static IReadOnlyList<SqlServerModuleChoice> Defaults() =>
        All.Select(module => new SqlServerModuleChoice(module.Id, true, module.DefaultIntervalSeconds)).ToList();

    /// <summary>The choice of one module, or its default when the list lacks it.</summary>
    /// <param name="modules">A server's choices.</param>
    /// <param name="module">The module.</param>
    /// <returns>The choice.</returns>
    public static SqlServerModuleChoice ChoiceOf(IReadOnlyList<SqlServerModuleChoice> modules, SqlServerModule module)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(module);

        return modules.FirstOrDefault(choice => choice.Id == module.Id)
            ?? new SqlServerModuleChoice(module.Id, true, module.DefaultIntervalSeconds);
    }

    /// <summary>Checks the modules an Admin chose and fills in the rest.</summary>
    /// <param name="input">The choices sent, or <see langword="null"/> to keep <paramref name="current"/>.</param>
    /// <param name="current">What the server has now; the defaults for a new one.</param>
    /// <returns>Every module's choice, in the catalog's order.</returns>
    /// <exception cref="AgentsValidationException">
    /// A choice names no module, repeats one, or breaks its rules.
    /// </exception>
    public static IReadOnlyList<SqlServerModuleChoice> Check(
        IReadOnlyList<SqlServerModuleChoice>? input,
        IReadOnlyList<SqlServerModuleChoice> current)
    {
        ArgumentNullException.ThrowIfNull(current);

        var chosen = new Dictionary<string, SqlServerModuleChoice>(StringComparer.Ordinal);

        foreach (var choice in input ?? [])
        {
            if (choice is null || Find(choice.Id) is not { } module)
            {
                throw new AgentsValidationException($"There is no module called {choice?.Id}.");
            }

            if (!chosen.TryAdd(module.Id, choice))
            {
                throw new AgentsValidationException($"The {module.Name} module is given twice.");
            }

            if (module.Required && !choice.Enabled)
            {
                throw new AgentsValidationException($"The {module.Name} module cannot be switched off.");
            }

            if (!module.Choices.Contains(choice.IntervalSeconds))
            {
                throw new AgentsValidationException(
                    $"The {module.Name} module reads every {Words(module.Choices)} seconds.");
            }
        }

        return All
            .Select(module => chosen.TryGetValue(module.Id, out var choice) ? choice : ChoiceOf(current, module))
            .ToList();
    }

    /// <summary>
    /// Reads the choices a server stored, leaving out a module that no longer exists and setting a choice that no
    /// longer fits back to the module's default, so a change to the catalog never breaks a server.
    /// </summary>
    /// <param name="stored">What was stored: the modules that differ from their defaults.</param>
    /// <returns>Every module's choice, in the catalog's order.</returns>
    public static IReadOnlyList<SqlServerModuleChoice> FromStored(IReadOnlyList<SqlServerModuleChoice> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        return All
            .Select(module => stored.FirstOrDefault(choice => choice.Id == module.Id) is { } choice
                ? new SqlServerModuleChoice(
                    module.Id,
                    choice.Enabled || module.Required,
                    module.Choices.Contains(choice.IntervalSeconds)
                        ? choice.IntervalSeconds
                        : module.DefaultIntervalSeconds)
                : new SqlServerModuleChoice(module.Id, true, module.DefaultIntervalSeconds))
            .ToList();
    }

    /// <summary>The choices that differ from their module's default, which is all a server stores.</summary>
    /// <param name="modules">Every module's choice.</param>
    /// <returns>The choices to store.</returns>
    public static IReadOnlyList<SqlServerModuleChoice> Overrides(IReadOnlyList<SqlServerModuleChoice> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        return modules
            .Where(choice => Find(choice.Id) is { } module
                && (!choice.Enabled || choice.IntervalSeconds != module.DefaultIntervalSeconds))
            .ToList();
    }

    private static SqlServerModule Module(
        string id,
        string name,
        string description,
        int defaultSeconds,
        int min,
        int max,
        bool required,
        params string[] receivers) =>
        new(
            id,
            name,
            description,
            defaultSeconds,
            _ladder.Where(seconds => seconds >= min && seconds <= max).ToList(),
            required,
            receivers);

    private static string Words(IReadOnlyList<int> choices) =>
        string.Join(", ", choices.Select(seconds => seconds.ToString(CultureInfo.InvariantCulture)));
}
