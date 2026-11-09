using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Access.Setup;
using Veyra.Access.Users;

namespace Veyra.Access.Tests.Users;

/// <summary>
/// Builds the Access services the way the module wires them, against one test database and a fake clock.
/// </summary>
internal sealed class AccessServices(NpgsqlDataSource database, FakeTimeProvider clock, AccessOptions? settings = null)
{
    private readonly IOptions<AccessOptions> _options = Options.Create(settings ?? new AccessOptions());
    private readonly PasswordHasher<AccessUser> _hasher = new();

    public UserStore Store { get; } = new(database);

    public UserService Users => new(
        Store,
        _hasher,
        new PasswordPolicy(_options),
        clock,
        NullLogger<UserService>.Instance);

    public SignInService SignIn => new(Store, _hasher, _options, clock, NullLogger<SignInService>.Instance);

    public SetupService CreateSetup() =>
        new(Store, _hasher, new PasswordPolicy(_options), _options, clock, NullLogger<SetupService>.Instance);
}
