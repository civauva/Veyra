using System.Globalization;
using Microsoft.Extensions.Options;
using Veyra.Access.Users;
using Veyra.Core;
using Veyra.Core.Settings;

namespace Veyra.Access.Settings;

/// <summary>
/// The Access module's settings on the Settings screen: sign-in, passwords, lockout, sessions, how long sign-in
/// history is kept (ADR 0017), and the look of the app (ADR 0021). The recovery switch for a lost second factor is
/// not one of them; it stays in the environment so it works when nobody can sign in.
/// </summary>
/// <param name="rules">The rules <see cref="AccessOptions"/> must meet.</param>
internal sealed class AccessSettings(IEnumerable<IValidateOptions<AccessOptions>> rules)
    : ModuleSettings<AccessOptions>(VeyraOptionNames.Access, rules)
{
    private const string _prefix = VeyraOptionNames.Access + ":";

    private static readonly AccessOptions _defaults = new();

    // What the Simple view leaves out: a switch that must stay on for now, and settings few installations change.
    private static readonly HashSet<string> _advanced =
    [
        _prefix + nameof(AccessOptions.LocalSignInEnabled),
        _prefix + nameof(AccessOptions.KeyCacheDuration),
        _prefix + nameof(AccessOptions.SetupTokenLifetime),
    ];

    /// <inheritdoc />
    public override IReadOnlyList<SettingDefinition> Definitions { get; } = Mark(
    [
        new(
            _prefix + nameof(AccessOptions.LocalSignInEnabled),
            SettingSection.Security,
            "Sign-in",
            "Sign in with a password",
            "Whether people sign in with their email and password. It stays on until single sign-on is set up.",
            SettingKind.Switch,
            "true")
        {
            Effect = SettingEffect.MayLockOut,
        },
        Duration(
            nameof(AccessOptions.SessionIdleTimeout),
            "Sessions",
            "End idle sessions after",
            "A session nobody used for this long ends, and its browser has to sign in again.",
            _defaults.SessionIdleTimeout,
            "h"),
        Whole(
            nameof(AccessOptions.MinimumPasswordLength),
            "Passwords",
            "Shortest password",
            "New passwords need at least this many characters. Existing passwords keep working.",
            _defaults.MinimumPasswordLength,
            "characters") with { Minimum = 12, Maximum = 128 },
        Whole(
            nameof(AccessOptions.LockoutThreshold),
            "Lockout",
            "Lock an account after",
            "Wrong passwords in a row before the account is locked for a while.",
            _defaults.LockoutThreshold,
            "wrong passwords") with { Effect = SettingEffect.MayLockOut },
        Duration(
            nameof(AccessOptions.LockoutDuration),
            "Lockout",
            "Lock it for",
            "How long a locked account waits before it can try again. An Admin can unlock it sooner.",
            _defaults.LockoutDuration,
            "min") with { Minimum = 0 },
        Whole(
            nameof(AccessOptions.SignInHistoryDays),
            "Sign-in history",
            "Keep sign-in history",
            "Sign-in attempts and ended sessions, with their addresses, older than this are deleted.",
            _defaults.SignInHistoryDays,
            "days") with
        {
            Maximum = 3650,
            Effect = SettingEffect.DeletesData,
            EnvironmentVariable = "VEYRA_SIGN_IN_HISTORY_DAYS",
        },
        new SettingDefinition(
            _prefix + nameof(AccessOptions.DefaultLook),
            SettingSection.Appearance,
            "Look",
            "Default look",
            "Used by people who have not picked a look, and by everyone when people may not choose.",
            SettingKind.Choice,
            _defaults.DefaultLook)
        {
            Choices = Looks.All,
        },
        new SettingDefinition(
            _prefix + nameof(AccessOptions.PeopleChooseLook),
            SettingSection.Appearance,
            "Look",
            "Let people choose their look",
            "When off, everyone sees the default look.",
            SettingKind.Switch,
            _defaults.PeopleChooseLook ? "true" : "false"),
        new SettingDefinition(
            _prefix + nameof(AccessOptions.KeyCacheDuration),
            SettingSection.Advanced,
            "Ingestion keys",
            "Remember a checked key for",
            "Senders' keys are checked against the database at most this often, so a revoked key stops working "
            + "within this time.",
            SettingKind.Duration,
            Text(_defaults.KeyCacheDuration))
        {
            Unit = "s",
            Minimum = 0,
        },
        new SettingDefinition(
            _prefix + nameof(AccessOptions.SetupTokenLifetime),
            SettingSection.Advanced,
            "First-run setup",
            "Setup link works for",
            "How long the link printed at first start can create the first Admin.",
            SettingKind.Duration,
            Text(_defaults.SetupTokenLifetime))
        {
            Unit = "h",
            Minimum = 1,
        },
    ]);

    /// <inheritdoc />
    public override Task<IReadOnlyList<SettingNote>> ReviewAsync(
        IReadOnlyList<SettingChange> changes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var notes = new List<SettingNote>();

        foreach (var change in changes)
        {
            var to = Number(change.To);

            if (change.Key == _prefix + nameof(AccessOptions.SignInHistoryDays)
                && to < (Number(change.From) ?? _defaults.SignInHistoryDays))
            {
                notes.Add(new SettingNote(
                    change.Key,
                    $"Sign-in history older than {to} days is deleted at the next hourly clean-up and cannot be "
                    + "brought back.",
                    Risky: true));
            }

            if (change.Key == _prefix + nameof(AccessOptions.MinimumPasswordLength)
                && to > (Number(change.From) ?? _defaults.MinimumPasswordLength))
            {
                notes.Add(new SettingNote(
                    change.Key,
                    $"Existing passwords keep working. New and changed passwords need {to} characters.",
                    Risky: false));
            }
        }

        return Task.FromResult<IReadOnlyList<SettingNote>>(notes);
    }

    private static List<SettingDefinition> Mark(IEnumerable<SettingDefinition> definitions) =>
        definitions.Select(definition => definition with { Advanced = _advanced.Contains(definition.Key) }).ToList();

    private static SettingDefinition Whole(
        string property,
        string group,
        string label,
        string help,
        int value,
        string unit) =>
        new(
            _prefix + property,
            SettingSection.Security,
            group,
            label,
            help,
            SettingKind.WholeNumber,
            value.ToString(CultureInfo.InvariantCulture))
        {
            Unit = unit,
            Minimum = 1,
        };

    private static SettingDefinition Duration(
        string property,
        string group,
        string label,
        string help,
        TimeSpan value,
        string unit) =>
        new(_prefix + property, SettingSection.Security, group, label, help, SettingKind.Duration, Text(value))
        {
            Unit = unit,
            Minimum = 1,
        };

    private static string Text(TimeSpan value) => value.ToString("c", CultureInfo.InvariantCulture);

    private static int? Number(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
}
