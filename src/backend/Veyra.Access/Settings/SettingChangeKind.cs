namespace Veyra.Access.Settings;

/// <summary>How a setting came to change, as the history shows it. The numbers are stored.</summary>
internal enum SettingChangeKind
{
    /// <summary>Changed on the Settings screen.</summary>
    Changed = 1,

    /// <summary>Reset to its default.</summary>
    Reset = 2,

    /// <summary>Set back to an earlier value from the history.</summary>
    Reverted = 3,

    /// <summary>Set from an imported file.</summary>
    Imported = 4,
}
