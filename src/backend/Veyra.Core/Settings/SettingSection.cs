namespace Veyra.Core.Settings;

/// <summary>The sections of the Settings screen, in the order it shows them (ADR 0017).</summary>
public enum SettingSection
{
    /// <summary>How long each kind of data is kept and how much room it may take.</summary>
    Data,

    /// <summary>The built-in alert rules and how often they run.</summary>
    Rules,

    /// <summary>The email server and how queued messages are sent.</summary>
    Email,

    /// <summary>Sign-in, passwords, lockout, and sessions.</summary>
    Security,

    /// <summary>The look of the web app: the default one and whether people pick their own (ADR 0021).</summary>
    Appearance,

    /// <summary>How much telemetry may wait to be written.</summary>
    Ingestion,

    /// <summary>Settings most installations never change.</summary>
    Advanced,
}
