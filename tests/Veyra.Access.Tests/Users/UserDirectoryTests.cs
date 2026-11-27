using Microsoft.Extensions.Time.Testing;
using Veyra.Access.History;
using Veyra.Access.Sessions;
using Veyra.Access.Users;
using Veyra.Core.Security;
using Veyra.Testing;

namespace Veyra.Access.Tests.Users;

/// <summary>What the Users screen and the Account page read about people (ADR 0011).</summary>
[Collection(PostgresGroup.Name)]
public class UserDirectoryTests(PostgresFixture postgres)
{
    private const string _password = "correct horse battery";

    private static readonly SignInClient _laptop = new(
        "198.51.100.7",
        "Mozilla/5.0 (X11; Ubuntu; Linux x86_64) Firefox/132.0");

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task ListAsync_SummarizesSecondFactorSessionsAndLastActivity()
    {
        var (access, admin, tom) = await CreateAsync();
        var setup = (await access.SecondFactor.StartSetupAsync(tom, CancellationToken.None))!;
        await access.SecondFactor.ConfirmSetupAsync(tom, access.CodeFor(setup.Secret), null, CancellationToken.None);
        await access.Sessions.StartAsync(tom.Id, SessionMethod.Code, _laptop, CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(10));
        await access.Sessions.StartAsync(tom.Id, SessionMethod.Code, _laptop, CancellationToken.None);

        var people = await access.Directory.ListAsync(CancellationToken.None);

        var row = Assert.Single(people, one => one.User.Id == tom.Id);
        Assert.True(row.SecondFactorSince is not null);
        Assert.Equal((2, _clock.GetUtcNow()), (row.ActiveSessions, row.LastActiveAt));
        Assert.Null(Assert.Single(people, one => one.User.Id == admin.Id).LastActiveAt);
    }

    [Fact]
    public async Task DetailAsync_HasSignInsSessionsHistoryAndWhoAddedThePerson()
    {
        var (access, admin, tom) = await CreateAsync();
        await access.SignIn.SignInAsync("tom@example.com", "wrong password!", _laptop, CancellationToken.None);
        var signedIn = await access.SignIn.SignInAsync("tom@example.com", _password, _laptop, CancellationToken.None);
        await access.Sessions.StartAsync(tom.Id, signedIn.Method, _laptop, CancellationToken.None);

        var detail = (await access.Directory.DetailAsync(tom.Id, CancellationToken.None))!;

        Assert.Equal("Dana Kim", detail.AddedBy);
        Assert.Equal([SignInOutcome.SignedIn, SignInOutcome.WrongPassword], detail.SignIns.Select(one => one.Outcome));
        Assert.Equal((1, 1), (detail.SignedInLast30Days, detail.FailedLast30Days));
        Assert.Single(detail.Sessions);
        Assert.Equal(UserEventKind.Added, Assert.Single(detail.Events).Kind);
        Assert.Null(detail.SecondFactor);
    }

    [Fact]
    public async Task SignInDaysAsync_MoreAttemptsThanTheDetailLists_CountsEveryOneOnThePersonsCalendar()
    {
        var (access, _, tom) = await CreateAsync();
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        for (var attempt = 0; attempt < UserDirectory.SignInsShown + 10; attempt++)
        {
            await access.SignInEvents.RecordAsync(
                tom.Id,
                _clock.GetUtcNow().AddMinutes(-attempt),
                SignInOutcome.WrongPassword,
                null,
                null,
                CancellationToken.None);
        }

        // 23:30 UTC on the 9th is half past midnight on the 10th in Berlin.
        await access.SignInEvents.RecordAsync(
            tom.Id,
            new DateTimeOffset(2026, 11, 9, 23, 30, 0, TimeSpan.Zero),
            SignInOutcome.SignedIn,
            null,
            null,
            CancellationToken.None);
        await access.SignInEvents.RecordAsync(
            tom.Id,
            new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero),
            SignInOutcome.SignedInWithCode,
            null,
            null,
            CancellationToken.None);
        await access.SignInEvents.RecordAsync(
            tom.Id,
            new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero),
            SignInOutcome.SignedIn,
            null,
            null,
            CancellationToken.None);

        var days = await access.Directory.SignInDaysAsync(tom.Id, berlin, CancellationToken.None);

        Assert.Equal(14, days.Count);
        Assert.Equal(new DateOnly(2026, 10, 28), days[0].Day);
        Assert.Equal(new SignInDay(new DateOnly(2026, 11, 10), 1, 60), days[^1]);
        Assert.Equal(new SignInDay(new DateOnly(2026, 11, 7), 1, 0), days[^4]);
        Assert.Equal((2, 60), (days.Sum(day => day.SignedIn), days.Sum(day => day.Failed)));
    }

    [Fact]
    public async Task DetailAsync_UnknownPerson_IsNull()
    {
        var (access, _, _) = await CreateAsync();

        Assert.Null(await access.Directory.DetailAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private async Task<(AccessServices Access, AccessUser Admin, AccessUser Tom)> CreateAsync()
    {
        var access = new AccessServices(await AccessDatabase.CreateAsync(postgres), _clock);
        var admin = await access.Users.CreateAsync(
            "dana@example.com",
            "Dana Kim",
            VeyraRole.Admin,
            _password,
            null,
            null,
            CancellationToken.None);
        var tom = await access.Users.CreateAsync(
            "tom@example.com",
            "Tom Okafor",
            VeyraRole.Editor,
            _password,
            "Payments",
            admin.Id,
            CancellationToken.None);

        return (access, admin, tom);
    }
}
