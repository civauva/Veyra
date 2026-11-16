namespace Veyra.Core.Settings;

/// <summary>
/// What changing a setting can do beyond itself. The Settings screen asks for an explicit confirmation before saving a
/// change with an effect (ADR 0017).
/// </summary>
public enum SettingEffect
{
    /// <summary>Nothing beyond the setting itself.</summary>
    None,

    /// <summary>Lowering it deletes stored data, which cannot be brought back.</summary>
    DeletesData,

    /// <summary>It can keep people from signing in.</summary>
    MayLockOut,
}
