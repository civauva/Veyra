using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Veyra.Access.Api;
using Veyra.Access.Users;
using Veyra.Core;
using Veyra.Core.Settings;

namespace Veyra.Access.Settings;

/// <summary>
/// Serves the Settings screen: every module's settings with their values and defaults, a review of changes before they
/// are saved, the save itself, the history with revert, and export and import (ADR 0017).
/// </summary>
/// <param name="modules">Every module's settings.</param>
/// <param name="configuration">The application's configuration, with saved values in effect.</param>
/// <param name="store">Where saved values and their history are kept.</param>
/// <param name="runtime">Refreshes the configuration after a save.</param>
/// <param name="users">Gives the name of who saves a change.</param>
/// <param name="environment">The host environment, for the System section.</param>
/// <param name="clock">Stamps exports.</param>
internal sealed class SettingsService(
    IEnumerable<IModuleSettings> modules,
    IConfiguration configuration,
    SettingStore store,
    SavedSettingsRuntime runtime,
    UserStore users,
    IHostEnvironment environment,
    TimeProvider clock)
{
    /// <summary>The <c>Format</c> of an exported file.</summary>
    public const string FileFormat = "veyra-settings";

    private readonly List<IModuleSettings> _modules = modules.ToList();

    private readonly Dictionary<string, SettingDefinition> _definitions = modules
        .SelectMany(module => module.Definitions)
        .ToDictionary(definition => definition.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The keys of a section's settings, for filtering the history by section.</summary>
    /// <param name="section">The section's name, such as <c>Data</c>.</param>
    /// <returns>The keys; empty for an unknown section.</returns>
    public IReadOnlyList<string> KeysOf(string section) =>
        Enum.TryParse<SettingSection>(section, ignoreCase: true, out var parsed)
            ? _definitions.Values.Where(definition => definition.Section == parsed).Select(d => d.Key).ToList()
            : [];

    /// <summary>
    /// The kinds of monitored systems a section of the Settings screen sets up, for filtering the history by section.
    /// They are not settings, so they have sections of their own: Database monitoring and Agents.
    /// </summary>
    /// <param name="section">The section's name, such as <c>Databases</c>.</param>
    /// <returns>The kinds; empty for a section of settings or an unknown one.</returns>
    public static IReadOnlyList<MonitoringSubject> SubjectsOf(string section) =>
        section.ToUpperInvariant() switch
        {
            "DATABASES" => [MonitoringSubject.SqlServer, MonitoringSubject.PostgresServer],
            "AGENTS" => [MonitoringSubject.Agent],
            _ => [],
        };

    /// <summary>Every setting in screen order, with its value, default, and who saved it.</summary>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page.</returns>
    public async Task<SettingsPageResponse> PageAsync(CancellationToken cancellationToken)
    {
        var saved = await store.ReadAllAsync(cancellationToken);
        var version = await store.VersionAsync(cancellationToken);

        var settings = _definitions.Values
            .OrderBy(definition => definition.Section)
            .Select(definition => Setting(definition, saved.GetValueOrDefault(definition.Key)))
            .ToList();

        return new SettingsPageResponse(version, settings, SystemSettings());
    }

    /// <summary>Checks changes and says what they would do, without saving anything.</summary>
    /// <param name="changes">The changes.</param>
    /// <param name="cancellationToken">Cancels any query the modules need.</param>
    /// <returns>The review.</returns>
    public async Task<SettingsReviewResponse> ReviewAsync(
        IReadOnlyList<SettingChangeRequest> changes,
        CancellationToken cancellationToken) =>
        (await PrepareAsync(changes, cancellationToken)).Review;

    /// <summary>Saves changes, all or nothing, if they pass the review.</summary>
    /// <param name="request">
    /// The changes, the version they were made from, and whether risky ones are confirmed.
    /// </param>
    /// <param name="userId">Who saves them.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The outcome.</returns>
    public async Task<SettingsSaveResult> SaveAsync(
        SettingsSaveRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kind = request.Origin is not null
            ? SettingChangeKind.Imported
            : request.Changes.All(change => change.Value is null) ? SettingChangeKind.Reset : SettingChangeKind.Changed;

        return await SaveAsync(
            request.Version,
            request.Changes,
            request.Confirmed,
            userId,
            kind,
            request.Origin,
            cancellationToken);
    }

    /// <summary>Sets a setting back to the value it had before a change in the history.</summary>
    /// <param name="id">The change's number.</param>
    /// <param name="request">The version the person saw, and whether a risky revert is confirmed.</param>
    /// <param name="userId">Who reverts it.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The outcome; null when there is no such change.</returns>
    public async Task<SettingsSaveResult?> RevertAsync(
        long id,
        SettingsRevertRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await store.GetChangeAsync(id, cancellationToken) is not { } change)
        {
            return null;
        }

        if (change.Secret || change.Subject is not null || !_definitions.ContainsKey(change.Name))
        {
            return SettingsSaveResult.Refused(new SettingsReviewResponse(
                [new SettingProblemResponse(change.Name, "This change cannot be set back from the history.")],
                [],
                [],
                false));
        }

        return await SaveAsync(
            request.Version,
            [new SettingChangeRequest(change.Name, change.OldValue)],
            request.Confirmed,
            userId,
            SettingChangeKind.Reverted,
            null,
            cancellationToken);
    }

    /// <summary>Reads a page of the history, newest first.</summary>
    /// <param name="query">Which changes to read.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page.</returns>
    public async Task<SettingsHistoryResponse> HistoryAsync(
        SettingHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var page = await store.HistoryAsync(query, cancellationToken);

        var changes = page.Changes.Select(change =>
        {
            if (change.Subject is { } subject)
            {
                return MonitoringItem(change, subject);
            }

            var definition = _definitions.GetValueOrDefault(change.Name);

            return new SettingHistoryItemResponse(
                change.Id,
                change.Name,
                definition?.Label ?? change.Name,
                definition?.Group,
                definition?.Section.ToString(),
                change.OldValue,
                change.NewValue,
                change.Secret,
                change.Kind.ToString(),
                change.ChangedBy,
                change.ChangedByName,
                change.ChangedAt,
                change.Origin,
                CanRevert: !change.Secret && definition is not null);
        }).ToList();

        return new SettingsHistoryResponse(changes, page.Next);
    }

    // A change to a monitored system or agent: named after it, with the fields it touched and no values. It cannot be
    // reverted from the history, because the old values, a password among them, were never kept.
    private static SettingHistoryItemResponse MonitoringItem(SettingChangeRecord change, MonitoringSubject subject) =>
        new(
            change.Id,
            change.Name,
            change.Name,
            subject switch
            {
                MonitoringSubject.SqlServer => "SQL Server",
                MonitoringSubject.PostgresServer => "PostgreSQL server",
                _ => "Agent",
            },
            subject == MonitoringSubject.Agent ? "Agents" : "Databases",
            null,
            null,
            false,
            change.Kind.ToString(),
            change.ChangedBy,
            change.ChangedByName,
            change.ChangedAt,
            null,
            CanRevert: false,
            Subject: subject.ToString(),
            Fields: change.Fields ?? []);

    /// <summary>Every saved value except secrets, as a file to import elsewhere.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The file.</returns>
    public async Task<SettingsFile> ExportAsync(CancellationToken cancellationToken)
    {
        var saved = await store.ReadAllAsync(cancellationToken);

        var values = saved
            .Where(pair => !pair.Value.Secret && _definitions.ContainsKey(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal);

        return new SettingsFile(FileFormat, 1, clock.GetUtcNow(), values);
    }

    /// <summary>Reviews what importing a file would change; nothing is saved.</summary>
    /// <param name="file">The file.</param>
    /// <param name="cancellationToken">Cancels any query the modules need.</param>
    /// <returns>The review, with only the settings whose value would change.</returns>
    public async Task<SettingsReviewResponse> ImportAsync(SettingsFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Format != FileFormat || file.FormatVersion != 1)
        {
            return new SettingsReviewResponse(
                [new SettingProblemResponse(null, "This is not a Veyra settings file.")],
                [],
                [],
                false);
        }

        var changes = file.Values
            .Where(
                pair => !_definitions.TryGetValue(
                    pair.Key,
                    out var definition) || definition.Kind != SettingKind.Secret)
            .Where(pair => configuration[pair.Key] != pair.Value)
            .Select(pair => new SettingChangeRequest(pair.Key, pair.Value))
            .ToList();

        return await ReviewAsync(changes, cancellationToken);
    }

    private async Task<SettingsSaveResult> SaveAsync(
        long version,
        IReadOnlyList<SettingChangeRequest> changes,
        bool confirmed,
        Guid userId,
        SettingChangeKind kind,
        string? origin,
        CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(changes, cancellationToken);

        if (prepared.Review.Problems.Count > 0 || (prepared.Review.ConfirmationRequired && !confirmed))
        {
            return SettingsSaveResult.Refused(prepared.Review);
        }

        var author = await users.FindByIdAsync(userId, cancellationToken);
        var saved = await store.SaveAsync(
            version,
            prepared.Writes,
            new SettingAuthor(userId, author?.DisplayName ?? "Unknown"),
            kind,
            origin,
            cancellationToken);

        if (saved is null)
        {
            return SettingsSaveResult.Conflict();
        }

        runtime.Refresh();

        return SettingsSaveResult.Saved(await PageAsync(cancellationToken));
    }

    // Normalizes each change, checks the candidate configuration with every module, and asks each module what its
    // changes will do.
    private async Task<(SettingsReviewResponse Review, List<SettingWrite> Writes)> PrepareAsync(
        IReadOnlyList<SettingChangeRequest> changes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var problems = new List<SettingProblemResponse>();
        var writes = new List<SettingWrite>();
        var diffs = new List<SettingChange>();
        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var change in changes)
        {
            if (!_definitions.TryGetValue(change.Key, out var definition))
            {
                problems.Add(new SettingProblemResponse(change.Key, "Veyra has no such setting."));
                continue;
            }

            var secret = definition.Kind == SettingKind.Secret;
            var reset = change.Value is null || (secret && change.Value.Length == 0);
            var (value, problem) = reset ? (null, null) : SettingValues.Normalize(definition, change.Value!);

            if (problem is not null)
            {
                problems.Add(new SettingProblemResponse(definition.Key, problem));
                continue;
            }

            var from = configuration[definition.Key] ?? Default(definition).Value;
            var to = reset ? Default(definition).Value : value;

            if (!secret && from == to && !reset)
            {
                continue;
            }

            overrides[definition.Key] = to;
            writes.Add(new SettingWrite(definition.Key, reset ? null : value, secret));
            diffs.Add(new SettingChange(definition.Key, secret ? null : from, secret ? null : to));
        }

        var candidate = new ConfigurationBuilder()
            .AddConfiguration(configuration)
            .AddInMemoryCollection(overrides)
            .Build();

        problems.AddRange(_modules
            .SelectMany(module => module.Validate(candidate))
            .Select(problem => new SettingProblemResponse(problem.Key, problem.Message)));

        var notes = new List<SettingNoteResponse>();

        if (problems.Count == 0)
        {
            foreach (var module in _modules)
            {
                var own = diffs.Where(
                    diff => module.Definitions.Any(d => d.Key.Equals(diff.Key, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (own.Count > 0)
                {
                    notes.AddRange((await module.ReviewAsync(own, cancellationToken))
                        .Select(note => new SettingNoteResponse(note.Key, note.Text, note.Risky)));
                }
            }

            // A change that may lock people out gets a warning even when its module says nothing more.
            notes.AddRange(diffs
                .Where(diff => _definitions[diff.Key].Effect == SettingEffect.MayLockOut
                    && notes.All(note => !note.Key.Equals(diff.Key, StringComparison.OrdinalIgnoreCase)))
                .Select(diff => new SettingNoteResponse(diff.Key, "This can keep people from signing in.", true)));
        }

        var review = new SettingsReviewResponse(
            problems,
            notes,
            diffs.Select(diff => new SettingDiffResponse(diff.Key, diff.From, diff.To)).ToList(),
            notes.Any(note => note.Risky));

        return (review, writes);
    }

    private SettingResponse Setting(SettingDefinition definition, SavedSetting? saved)
    {
        var secret = definition.Kind == SettingKind.Secret;
        var (defaultValue, source) = Default(definition);
        var value = configuration[definition.Key] ?? defaultValue;

        return new SettingResponse(
            definition.Key,
            definition.Section.ToString(),
            definition.Group,
            definition.Label,
            definition.Help,
            definition.Kind.ToString(),
            definition.Unit,
            definition.Minimum,
            definition.Maximum,
            definition.Choices,
            definition.Effect.ToString(),
            definition.Optional,
            definition.Advanced,
            definition.EnvironmentVariable,
            secret ? null : value,
            !string.IsNullOrEmpty(value),
            secret ? null : defaultValue,
            source,
            saved is not null,
            saved?.ChangedByName,
            saved?.ChangedAt);
    }

    // The value without a saved one: the last source before the saved settings that sets it, or the code.
    private (string? Value, SettingSourceResponse Source) Default(SettingDefinition definition)
    {
        if (configuration is IConfigurationRoot root)
        {
            foreach (var provider in root.Providers.Reverse())
            {
                if (provider is SavedSettingsConfigurationProvider
                    || !provider.TryGet(definition.Key, out var value))
                {
                    continue;
                }

                return (value, provider switch
                {
                    EnvironmentVariablesConfigurationProvider => new SettingSourceResponse(
                        "environment",
                        definition.EnvironmentVariable ?? definition.Key.Replace(":", "__", StringComparison.Ordinal)),
                    JsonConfigurationProvider json => new SettingSourceResponse(
                        "file",
                        Path.GetFileName(json.Source.Path)),
                    _ => new SettingSourceResponse("configuration", null),
                });
            }
        }

        return (definition.Default, new SettingSourceResponse("code", null));
    }

    // What is set where Veyra is installed, without passwords.
    private List<SystemSettingResponse> SystemSettings()
    {
        var items = new List<SystemSettingResponse>();

        if (configuration[$"{VeyraOptionNames.Storage}:ConnectionString"] is { Length: > 0 } connectionString)
        {
            var database = new NpgsqlConnectionStringBuilder(connectionString);
            items.Add(new SystemSettingResponse(
                "Database",
                $"{database.Database} on {database.Host}:{database.Port}",
                "deploy/.env (POSTGRES_PASSWORD) and the Compose file"));
        }

        var endpoints = configuration.GetSection("Kestrel:Endpoints").GetChildren()
            .Select(endpoint => $"{endpoint.Key} {endpoint["Url"]}")
            .ToList();
        items.Add(new SystemSettingResponse(
            "Listens on",
            endpoints.Count > 0 ? string.Join(", ", endpoints) : "the defaults of ASP.NET Core",
            "the Dockerfile; ports are mapped in deploy/.env"));

        items.Add(new SystemSettingResponse(
            "Encryption keys",
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true"
                ? "the veyra-keys volume"
                : "the profile of the user running Veyra",
            "ASP.NET Core Data Protection; back them up with the database"));

        items.Add(new SystemSettingResponse(
            "Lost second factor",
            string.IsNullOrWhiteSpace(configuration[$"{VeyraOptionNames.Access}:RemoveSecondFactor"])
                ? "not set"
                : "set; restart without it once the person has signed in",
            "deploy/.env (VEYRA_REMOVE_SECOND_FACTOR)"));

        items.Add(new SystemSettingResponse("Environment", environment.EnvironmentName, "ASPNETCORE_ENVIRONMENT"));
        items.Add(new SystemSettingResponse(
            "Version",
            Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion
                ?? "unknown",
            "the image"));

        return items;
    }
}
