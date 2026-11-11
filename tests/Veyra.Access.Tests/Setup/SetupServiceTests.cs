using Microsoft.Extensions.Time.Testing;
using Veyra.Access.Setup;
using Veyra.Access.Tests.Users;
using Veyra.Core.Security;
using Veyra.Testing;

namespace Veyra.Access.Tests.Setup;

[Collection(PostgresGroup.Name)]
public class SetupServiceTests(PostgresFixture postgres)
{
    private const string _password = "correct horse battery";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task PrepareAsync_NoUsers_IssuesAToken()
    {
        var setup = new AccessServices(await AccessDatabase.CreateAsync(postgres), _clock).CreateSetup();

        await setup.PrepareAsync(CancellationToken.None);

        Assert.NotNull(setup.Token);
    }

    [Fact]
    public async Task PrepareAsync_UsersExist_IssuesNoToken()
    {
        var access = new AccessServices(await AccessDatabase.CreateAsync(postgres), _clock);
        await access.Users.CreateAsync(
            "root@example.com",
            "Root",
            VeyraRole.Admin,
            _password,
            null,
            null,
            CancellationToken.None);
        var setup = access.CreateSetup();

        await setup.PrepareAsync(CancellationToken.None);

        Assert.Null(setup.Token);
    }

    [Fact]
    public async Task CompleteAsync_ValidToken_CreatesTheAdminAndUsesUpTheToken()
    {
        var access = new AccessServices(await AccessDatabase.CreateAsync(postgres), _clock);
        var setup = access.CreateSetup();
        await setup.PrepareAsync(CancellationToken.None);
        var token = setup.Token!;

        var first = await setup.CompleteAsync(token, "root@example.com", "Root", _password, CancellationToken.None);
        var second = await setup.CompleteAsync(token, "evil@example.com", "Evil", _password, CancellationToken.None);

        Assert.Equal(SetupResult.Created, first);
        Assert.Equal(SetupResult.InvalidToken, second);
        var admin = await access.Store.FindByEmailAsync("root@example.com", CancellationToken.None);
        Assert.Equal(VeyraRole.Admin, admin!.Role);
    }

    [Fact]
    public async Task CompleteAsync_WrongToken_ReturnsInvalidToken()
    {
        var setup = new AccessServices(await AccessDatabase.CreateAsync(postgres), _clock).CreateSetup();
        await setup.PrepareAsync(CancellationToken.None);

        var result = await setup.CompleteAsync(
            "not-the-token",
            "root@example.com",
            "Root",
            _password,
            CancellationToken.None);

        Assert.Equal(SetupResult.InvalidToken, result);
    }

    [Fact]
    public async Task CompleteAsync_AfterTheTokenExpired_ReturnsInvalidToken()
    {
        var setup = new AccessServices(await AccessDatabase.CreateAsync(postgres), _clock).CreateSetup();
        await setup.PrepareAsync(CancellationToken.None);
        var token = setup.Token!;

        _clock.Advance(TimeSpan.FromHours(25));

        var result = await setup.CompleteAsync(token, "root@example.com", "Root", _password, CancellationToken.None);

        Assert.Equal(SetupResult.InvalidToken, result);
    }

    [Fact]
    public async Task CompleteAsync_UsersAlreadyExist_ReturnsAlreadyCompleted()
    {
        var access = new AccessServices(await AccessDatabase.CreateAsync(postgres), _clock);
        var setup = access.CreateSetup();
        await setup.PrepareAsync(CancellationToken.None);
        var token = setup.Token!;

        // Someone created a user another way between issuing the token and using it.
        await access.Users.CreateAsync(
            "ops@example.com",
            "Ops",
            VeyraRole.Admin,
            _password,
            null,
            null,
            CancellationToken.None);

        var result = await setup.CompleteAsync(token, "root@example.com", "Root", _password, CancellationToken.None);

        Assert.Equal(SetupResult.AlreadyCompleted, result);
    }
}
