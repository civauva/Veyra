namespace Veyra.Access.Api;

/// <summary>One setting to change, as the Settings screen sends it.</summary>
/// <param name="Key">The configuration key, such as <c>Veyra:Storage:LogRetentionDays</c>.</param>
/// <param name="Value">The new value as configuration writes it; null to reset it to its default.</param>
public sealed record SettingChangeRequest(string Key, string? Value);

/// <summary>Changes to check before saving.</summary>
/// <param name="Version">The settings version the changes were made from.</param>
/// <param name="Changes">The changes.</param>
public sealed record SettingsReviewRequest(long Version, IReadOnlyList<SettingChangeRequest> Changes);

/// <summary>Changes to save, all or nothing.</summary>
/// <param name="Version">
/// The settings version the changes were made from; another save in between is a conflict.
/// </param>
/// <param name="Changes">The changes.</param>
/// <param name="Confirmed">Whether the person confirmed changes that delete data or may lock people out.</param>
/// <param name="Origin">The file name when the changes come from an import; null otherwise.</param>
public sealed record SettingsSaveRequest(
    long Version,
    IReadOnlyList<SettingChangeRequest> Changes,
    bool Confirmed = false,
    string? Origin = null);

/// <summary>A request to set a setting back to the value it had before a change in the history.</summary>
/// <param name="Version">The settings version the person saw.</param>
/// <param name="Confirmed">Whether the person confirmed it, when it deletes data or may lock people out.</param>
public sealed record SettingsRevertRequest(long Version, bool Confirmed = false);

/// <summary>Saved settings in a file, as exported and imported.</summary>
/// <param name="Format">Always <c>veyra-settings</c>.</param>
/// <param name="FormatVersion">The version of the file format; 1.</param>
/// <param name="ExportedAt">When the file was exported.</param>
/// <param name="Values">The saved values by key. Secrets are never in the file.</param>
public sealed record SettingsFile(
    string Format,
    int FormatVersion,
    DateTimeOffset? ExportedAt,
    IReadOnlyDictionary<string, string> Values);

/// <summary>Where a setting's default comes from.</summary>
/// <param name="Kind">
/// <c>code</c>, <c>file</c> (<c>appsettings.json</c>), <c>environment</c>, or <c>configuration</c> for any other
/// source.
/// </param>
/// <param name="Name">The variable or file that sets it; null for the code.</param>
public sealed record SettingSourceResponse(string Kind, string? Name);

/// <summary>One setting on the Settings screen.</summary>
/// <param name="Key">The configuration key.</param>
/// <param name="Section">The section that shows it.</param>
/// <param name="Group">The card it sits on.</param>
/// <param name="Label">A short name.</param>
/// <param name="Help">What it does.</param>
/// <param name="Kind">How it is edited: WholeNumber, Number, Ratio, Duration, Switch, Text, Choice, or Secret.</param>
/// <param name="Unit">The unit shown; for a ratio the shown value is a percentage, for a duration this unit.</param>
/// <param name="Minimum">The lowest value allowed, in the unit shown.</param>
/// <param name="Maximum">The highest value allowed, in the unit shown.</param>
/// <param name="Choices">The values a choice can take.</param>
/// <param name="Effect">None, DeletesData, or MayLockOut.</param>
/// <param name="Optional">Whether it may be left empty.</param>
/// <param name="Advanced">Whether it shows only in the Advanced view.</param>
/// <param name="EnvironmentVariable">The variable in <c>deploy/.env</c> that gives its first value.</param>
/// <param name="Value">The value in effect; always null for a secret.</param>
/// <param name="IsSet">Whether it has a value; for a secret, the only thing shown.</param>
/// <param name="Default">The value without a saved one; always null for a secret.</param>
/// <param name="DefaultSource">Where the default comes from.</param>
/// <param name="Saved">Whether a value is saved on the Settings screen.</param>
/// <param name="SavedBy">Who saved it last.</param>
/// <param name="SavedAt">When it was saved last.</param>
public sealed record SettingResponse(
    string Key,
    string Section,
    string Group,
    string Label,
    string Help,
    string Kind,
    string? Unit,
    double? Minimum,
    double? Maximum,
    IReadOnlyList<string> Choices,
    string Effect,
    bool Optional,
    bool Advanced,
    string? EnvironmentVariable,
    string? Value,
    bool IsSet,
    string? Default,
    SettingSourceResponse DefaultSource,
    bool Saved,
    string? SavedBy,
    DateTimeOffset? SavedAt);

/// <summary>A value set where Veyra is installed, shown read-only.</summary>
/// <param name="Label">What it is.</param>
/// <param name="Value">Its value, without passwords.</param>
/// <param name="Source">Where it is set.</param>
public sealed record SystemSettingResponse(string Label, string Value, string Source);

/// <summary>Every setting, and the version to save changes from.</summary>
/// <param name="Version">The settings version.</param>
/// <param name="Settings">Every setting, in screen order.</param>
/// <param name="System">What is set where Veyra is installed.</param>
public sealed record SettingsPageResponse(
    long Version,
    IReadOnlyList<SettingResponse> Settings,
    IReadOnlyList<SystemSettingResponse> System);

/// <summary>Why a change cannot be saved.</summary>
/// <param name="Key">The setting to fix; null when it concerns no single one.</param>
/// <param name="Message">What is wrong.</param>
public sealed record SettingProblemResponse(string? Key, string Message);

/// <summary>What a change will do.</summary>
/// <param name="Key">The setting.</param>
/// <param name="Text">What will happen.</param>
/// <param name="Risky">Whether it deletes data or may lock people out.</param>
public sealed record SettingNoteResponse(string Key, string Text, bool Risky);

/// <summary>A change as the review shows it.</summary>
/// <param name="Key">The setting.</param>
/// <param name="From">The value in effect now; null for a secret or when unset.</param>
/// <param name="To">The value after; null for a secret or when it becomes unset.</param>
public sealed record SettingDiffResponse(string Key, string? From, string? To);

/// <summary>The review of changes before they are saved.</summary>
/// <param name="Problems">Why they cannot be saved; empty when they can.</param>
/// <param name="Notes">What they will do.</param>
/// <param name="Changes">The changes that would change something.</param>
/// <param name="ConfirmationRequired">Whether saving needs the person's explicit confirmation.</param>
public sealed record SettingsReviewResponse(
    IReadOnlyList<SettingProblemResponse> Problems,
    IReadOnlyList<SettingNoteResponse> Notes,
    IReadOnlyList<SettingDiffResponse> Changes,
    bool ConfirmationRequired);

/// <summary>One change in the settings history: to a setting, or to a monitored system or agent (ADR 0019).</summary>
/// <param name="Id">The change's number.</param>
/// <param name="Key">The setting, or the name of the monitored system or agent.</param>
/// <param name="Label">The setting's label, its key when Veyra no longer has it, or the system's name.</param>
/// <param name="Group">The setting's card, or the kind of system, such as <c>SQL Server</c>.</param>
/// <param name="Section">The setting's section; <c>Databases</c> or <c>Agents</c> for a monitored system.</param>
/// <param name="OldValue">The value before; null for a secret, a monitored system, or when none was saved.</param>
/// <param name="NewValue">The value after; null for a secret, a monitored system, or when reset.</param>
/// <param name="Secret">Whether its values are hidden.</param>
/// <param name="Kind">
/// Changed, Reset, Reverted, or Imported for a setting; Added, Changed, or Removed for a monitored system.
/// </param>
/// <param name="ChangedBy">The account that saved it, when it still exists.</param>
/// <param name="ChangedByName">Who saved it.</param>
/// <param name="ChangedAt">When.</param>
/// <param name="Origin">The imported file's name.</param>
/// <param name="CanRevert">Whether it can be set back from the history; never for a monitored system.</param>
/// <param name="Subject">
/// <c>SqlServer</c>, <c>PostgresServer</c>, or <c>Agent</c> for a monitored system; null for a setting.
/// </param>
/// <param name="Fields">For a monitored system, the fields a change touched, never their values; null otherwise.</param>
public sealed record SettingHistoryItemResponse(
    long Id,
    string Key,
    string Label,
    string? Group,
    string? Section,
    string? OldValue,
    string? NewValue,
    bool Secret,
    string Kind,
    Guid? ChangedBy,
    string ChangedByName,
    DateTimeOffset ChangedAt,
    string? Origin,
    bool CanRevert,
    string? Subject = null,
    IReadOnlyList<string>? Fields = null);

/// <summary>A page of the settings history.</summary>
/// <param name="Changes">The changes, newest first.</param>
/// <param name="Next">The value of <c>before</c> for the next page; null on the last.</param>
public sealed record SettingsHistoryResponse(IReadOnlyList<SettingHistoryItemResponse> Changes, long? Next);
