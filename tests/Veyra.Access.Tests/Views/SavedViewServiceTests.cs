using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Access.Views;
using Veyra.Testing;

namespace Veyra.Access.Tests.Views;

/// <summary>Saved views of the Log explorer (ADR 0024): each person's own, and those others share.</summary>
[Collection(PostgresGroup.Name)]
public class SavedViewServiceTests(PostgresFixture postgres)
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task CreateAsync_View_IsListedForItsOwner()
    {
        var (views, ana, _) = await CreateAsync();

        var created = await views.CreateAsync(ana, "logs", "  Slow payments ", "service:payments level>=warn", false, default);
        var listed = Assert.Single(await views.ListAsync(ana, "logs", default));

        Assert.Equal(created, listed);
        Assert.Equal(
            ("Slow payments", "service:payments level>=warn", false, "Ana", _clock.GetUtcNow()),
            (listed.Name, listed.Query, listed.Shared, listed.OwnerName, listed.UpdatedAt));
    }

    [Fact]
    public async Task ListAsync_OthersViews_ShowsOnlyThoseTheyShared()
    {
        var (views, ana, bo) = await CreateAsync();
        await views.CreateAsync(bo, "logs", "Bo's private", "", false, default);
        await views.CreateAsync(bo, "logs", "Bo's shared", "level:error", true, default);
        await views.CreateAsync(ana, "logs", "Ana's own", "deadlock", false, default);

        var seen = await views.ListAsync(ana, "logs", default);

        Assert.Equal(["Ana's own", "Bo's shared"], seen.Select(view => view.Name));
        Assert.Equal("Bo", seen[1].OwnerName);
        Assert.Equal(bo, seen[1].OwnerId);
    }

    [Fact]
    public async Task UpdateAsync_Owner_ChangesNameQueryAndSharing()
    {
        var (views, ana, _) = await CreateAsync();
        var view = await views.CreateAsync(ana, "logs", "Errors", "level:error", false, default);
        _clock.Advance(TimeSpan.FromMinutes(5));

        var (change, updated) = await views.UpdateAsync(ana, view.Id, "Fatal", "level:fatal", true, default);

        Assert.Equal(SavedViewChange.Done, change);
        Assert.Equal(("Fatal", "level:fatal", true), (updated!.Name, updated.Query, updated.Shared));
        Assert.Equal((view.CreatedAt, _clock.GetUtcNow()), (updated.CreatedAt, updated.UpdatedAt));
    }

    [Fact]
    public async Task UpdateAndDelete_SomeoneElsesSharedView_AreRefusedAsNotTheOwner()
    {
        var (views, ana, bo) = await CreateAsync();
        var shared = await views.CreateAsync(bo, "logs", "Shared", "", true, default);

        var (update, _) = await views.UpdateAsync(ana, shared.Id, "Mine now", "", true, default);
        var delete = await views.DeleteAsync(ana, shared.Id, default);

        Assert.Equal((SavedViewChange.NotOwner, SavedViewChange.NotOwner), (update, delete));
        Assert.Equal("Shared", Assert.Single(await views.ListAsync(bo, "logs", default)).Name);
    }

    [Fact]
    public async Task UpdateAndDelete_SomeoneElsesPrivateView_AreNotFound()
    {
        var (views, ana, bo) = await CreateAsync();
        var hidden = await views.CreateAsync(bo, "logs", "Hidden", "", false, default);

        var (update, _) = await views.UpdateAsync(ana, hidden.Id, "Found", "", false, default);
        var delete = await views.DeleteAsync(ana, hidden.Id, default);
        var missing = await views.DeleteAsync(ana, Guid.CreateVersion7(), default);

        Assert.Equal(
            (SavedViewChange.NotFound, SavedViewChange.NotFound, SavedViewChange.NotFound),
            (update, delete, missing));
    }

    [Fact]
    public async Task DeleteAsync_Owner_RemovesTheView()
    {
        var (views, ana, _) = await CreateAsync();
        var view = await views.CreateAsync(ana, "logs", "Gone soon", "", false, default);

        Assert.Equal(SavedViewChange.Done, await views.DeleteAsync(ana, view.Id, default));
        Assert.Empty(await views.ListAsync(ana, "logs", default));
    }

    [Theory]
    [InlineData("logs", "", "")]
    [InlineData("logs", "   ", "")]
    [InlineData("traces", "Name", "")]
    public async Task CreateAsync_BadNameOrScreen_IsRefused(string screen, string name, string query)
    {
        var (views, ana, _) = await CreateAsync();

        await Assert.ThrowsAsync<AccessValidationException>(() =>
            views.CreateAsync(ana, screen, name, query, false, default));
    }

    [Fact]
    public async Task CreateAsync_TooLongNameOrQuery_IsRefused()
    {
        var (views, ana, _) = await CreateAsync();

        await Assert.ThrowsAsync<AccessValidationException>(() =>
            views.CreateAsync(ana, "logs", new string('n', 81), "", false, default));
        await Assert.ThrowsAsync<AccessValidationException>(() =>
            views.CreateAsync(ana, "logs", "Long", new string('q', 2_001), false, default));
        Assert.Equal(
            80,
            (await views.CreateAsync(ana, "logs", new string('n', 80), new string('q', 2_000), false, default))
                .Name.Length);
    }

    [Fact]
    public async Task CreateAsync_HundredViewsAlready_IsAConflict()
    {
        var (views, ana, bo) = await CreateAsync();

        for (var number = 0; number < SavedViewService.MaxViewsPerPerson; number++)
        {
            await views.CreateAsync(ana, "logs", $"View {number}", "", false, default);
        }

        await Assert.ThrowsAsync<AccessConflictException>(() =>
            views.CreateAsync(ana, "logs", "One more", "", false, default));
        Assert.NotNull(await views.CreateAsync(bo, "logs", "Bo has room", "", false, default));
    }

    [Fact]
    public async Task ListAsync_UnknownScreen_IsRefused()
    {
        var (views, ana, _) = await CreateAsync();

        await Assert.ThrowsAsync<AccessValidationException>(() => views.ListAsync(ana, "home", default));
    }

    private async Task<(SavedViewService Views, Guid Ana, Guid Bo)> CreateAsync()
    {
        var database = await AccessDatabase.CreateAsync(postgres);
        var ana = await SeedUserAsync(database, "ana@example.com", "Ana");
        var bo = await SeedUserAsync(database, "bo@example.com", "Bo");

        return (new SavedViewService(new SavedViewStore(database), _clock), ana, bo);
    }

    private static async Task<Guid> SeedUserAsync(NpgsqlDataSource database, string email, string name)
    {
        var id = Guid.CreateVersion7();

        await using var command = database.CreateCommand(
            """
            INSERT INTO access.user_account (id, email, email_normalized, display_name, role, created_at)
            VALUES ($1, $2, $2, $3, 1, now())
            """);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(email);
        command.Parameters.AddWithValue(name);
        await command.ExecuteNonQueryAsync();

        return id;
    }
}
