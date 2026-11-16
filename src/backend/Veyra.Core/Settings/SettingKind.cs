namespace Veyra.Core.Settings;

/// <summary>What kind of value a setting holds, which decides how the Settings screen edits and shows it.</summary>
public enum SettingKind
{
    /// <summary>A whole number, such as days or a count.</summary>
    WholeNumber,

    /// <summary>A number that may have decimals, such as seconds or gigabytes.</summary>
    Number,

    /// <summary>A share stored from 0 to 1 and shown as a percentage, such as an error ratio.</summary>
    Ratio,

    /// <summary>A length of time, stored as <c>d.hh:mm:ss</c> and shown in the setting's unit.</summary>
    Duration,

    /// <summary>On or off.</summary>
    Switch,

    /// <summary>Free text, such as a host name or an address.</summary>
    Text,

    /// <summary>One of the setting's <see cref="SettingDefinition.Choices"/>.</summary>
    Choice,

    /// <summary>Text that is stored encrypted and never shown again, such as a password.</summary>
    Secret,
}
