using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Access.History;
using Veyra.Access.SecondFactor;
using Veyra.Access.Sessions;
using Veyra.Access.Settings;
using Veyra.Access.Setup;
using Veyra.Access.Users;

namespace Veyra.Access.Tests.Users;

/// <summary>The Access services over a test database, wired as the module wires them.</summary>
internal sealed class AccessServices(NpgsqlDataSource database, FakeTimeProvider clock, AccessOptions? settings = null)
{
    private readonly IOptions<AccessOptions> _options = Options.Create(settings ?? new AccessOptions());
    private readonly PasswordHasher<AccessUser> _hasher = new();

    // One key ring for the whole test, as one Veyra installation has.
    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();

    public UserStore Store { get; } = new(database);

    public SessionStore SessionStore { get; } = new(database);

    public SignInEventStore SignInEvents { get; } = new(database);

    public UserEventStore UserEvents { get; } = new(database);

    public SecondFactorStore SecondFactorStore { get; } = new(database);

    public AccessSettingStore Settings { get; } = new(database);

    public SessionService Sessions => new(SessionStore, UserEvents, _options, clock);

    public SecondFactorService SecondFactor => new(
        SecondFactorStore,
        Settings,
        UserEvents,
        SessionStore,
        _protection,
        clock,
        NullLogger<SecondFactorService>.Instance);

    public UserService Users => new(
        Store,
        UserEvents,
        SessionStore,
        _hasher,
        new PasswordPolicy(_options),
        clock,
        NullLogger<UserService>.Instance);

    public SignInService SignIn => new(
        Store,
        SignInEvents,
        SecondFactor,
        _hasher,
        _options,
        clock,
        NullLogger<SignInService>.Instance);

    public UserDirectory Directory => new(
        Store,
        SessionStore,
        SignInEvents,
        UserEvents,
        SecondFactorStore,
        Settings,
        _options,
        clock);

    public AccessCleanup Cleanup => new(SignInEvents, SessionStore, _options, clock);

    public SetupService CreateSetup() =>
        new(
            Store,
            UserEvents,
            _hasher,
            new PasswordPolicy(_options),
            _options,
            clock,
            NullLogger<SetupService>.Instance);

    /// <summary>The current code of a person's authenticator app, as the phone would show it.</summary>
    public string CodeFor(string base32Secret, TimeSpan offset = default) =>
        Totp.Code(Base32.Decode(base32Secret), Totp.StepAt(clock.GetUtcNow() + offset));
}
