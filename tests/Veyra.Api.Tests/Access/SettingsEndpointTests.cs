using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Veyra.Storage;
using Veyra.Testing;

namespace Veyra.Api.Tests.Access;

/// <summary>The Settings screen's API (ADR 0017), against the whole application and a real database.</summary>
[Collection(PostgresGroup.Name)]
public class SettingsEndpointTests(PostgresFixture postgres)
{
    private const string _days = "Veyra:Storage:LogRetentionDays";

    private const string _password = "Veyra:Alerting:Notifications:Smtp:Password";

    [Fact]
    public async Task Get_AsAdmin_ListsEverySettingWithItsDefaultAndWhereItComesFrom()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        var settings = await admin.GetFromJsonAsync<SettingsPage>("/api/v1/settings");

        var days = settings!.Settings.Single(setting => setting.Key == _days);
        Assert.Equal(("14", "14", "code", false), (days.Value, days.Default, days.DefaultSource.Kind, days.Saved));
        Assert.Equal("Data", days.Section);
        Assert.False(days.Advanced);
        Assert.True(settings.Settings.Single(setting => setting.Key == "Veyra:Query:MaxPageSize").Advanced);
        var interval = settings.Settings.Single(setting => setting.Key == "Veyra:Alerting:EvaluationInterval");
        Assert.Equal(("01:00:00", "configuration"), (interval.Default, interval.DefaultSource.Kind));
        Assert.Contains(settings.System, item => item.Label == "Database");

        // The server counts the rules and those switched on (VEY-68).
        var switches = settings.Settings
            .Where(setting => setting.Section == "Rules" && setting.Label == "On")
            .ToList();
        Assert.True(settings.Rules > 0);
        Assert.Equal(
            (switches.Count, switches.Count(setting => setting.Value == "true")),
            (settings.Rules, settings.RulesOn));
        Assert.DoesNotContain(
            settings.System,
            item => item.Value.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Get_AsViewer_IsForbidden()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var viewer = await CreateViewerAsync(factory, admin);

        using var response = await viewer.GetAsync(new Uri("/api/v1/settings", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Listing_SimpleSectionAndSearch_ListWhatTheServerKeeps()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        var simple = await admin.GetFromJsonAsync<Listing>("/api/v1/settings/listing?section=Advanced");
        var advanced = await admin.GetFromJsonAsync<Listing>("/api/v1/settings/listing?section=Advanced&view=advanced");
        var found = await admin.GetFromJsonAsync<Listing>("/api/v1/settings/listing?search=maxpagesize&section=Data");

        Assert.Contains("Veyra:Query:MaxPageSize", advanced!.Keys);
        Assert.Equal(advanced.Keys, simple!.Keys);
        Assert.Equal(["Veyra:Query:MaxPageSize"], found!.Keys);
        Assert.DoesNotContain("System", simple.Sections);
        Assert.Contains("System", advanced.Sections);
    }

    [Fact]
    public async Task Listing_UnknownView_IsBadRequest()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri("/api/v1/settings/listing?view=fancy", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_ValidChange_IsSavedWithItsAuthor_AndAppliesAtOnce()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var before = await admin.GetFromJsonAsync<SettingsPage>("/api/v1/settings");

        using var response = await admin.PutAsJsonAsync(
            "/api/v1/settings",
            new { version = before!.Version, changes = new[] { new { key = _days, value = "30" } } });

        response.EnsureSuccessStatusCode();
        var after = (await response.Content.ReadFromJsonAsync<SettingsPage>())!.Settings.Single(s => s.Key == _days);
        Assert.Equal(("30", true, "Root"), (after.Value, after.Saved, after.SavedBy));
        Assert.Equal(
            30,
            factory.Services.GetRequiredService<IOptionsMonitor<StorageOptions>>().CurrentValue.LogRetentionDays);
    }

    [Fact]
    public async Task Put_SessionIdleTimeout_ReachesTheSessionCookieWithoutARestart()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        await SaveAsync(admin, ("Veyra:Access:SessionIdleTimeout", "1.00:00:00"));

        var cookie = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        Assert.Equal(TimeSpan.FromDays(1), cookie.ExpireTimeSpan);
    }

    [Fact]
    public async Task Put_ValueABrokenRule_IsRefusedWithAProblemForItsKey()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var version = (await admin.GetFromJsonAsync<SettingsPage>("/api/v1/settings"))!.Version;

        using var response = await admin.PutAsJsonAsync(
            "/api/v1/settings",
            new { version, changes = new[] { new { key = "Veyra:Alerting:ErrorRate:CautionRatio", value = "0.2" } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var review = await response.Content.ReadFromJsonAsync<Review>();
        Assert.Contains("Veyra:Alerting:ErrorRate:WarningRatio", review!.Problems.Select(problem => problem.Key));
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("0")]
    [InlineData("99999")]
    public async Task Review_ValueOfTheWrongKindOrOutOfRange_IsAProblem(string value)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        var review = await ReviewAsync(admin, (_days, value));

        Assert.Equal(_days, Assert.Single(review.Problems).Key);
    }

    [Fact]
    public async Task Put_RiskyChangeWithoutConfirmation_IsRefused_AndWithItSaved()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var version = (await admin.GetFromJsonAsync<SettingsPage>("/api/v1/settings"))!.Version;
        var changes = new[] { new { key = "Veyra:Alerting:HistoryDays", value = "30" } };

        using var unconfirmed = await admin.PutAsJsonAsync("/api/v1/settings", new { version, changes });
        using var confirmed = await admin.PutAsJsonAsync(
            "/api/v1/settings",
            new { version, changes, confirmed = true });

        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.True((await unconfirmed.Content.ReadFromJsonAsync<Review>())!.ConfirmationRequired);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
    }

    [Fact]
    public async Task Review_ShorterHistory_SaysWhatItDeletes_AndNeedsConfirmation()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        var review = await ReviewAsync(admin, ("Veyra:Alerting:HistoryDays", "30"));

        var note = Assert.Single(review.Notes);
        Assert.True(note.Risky && review.ConfirmationRequired);
        Assert.Equal(("90", "30"), (review.Changes.Single().From, review.Changes.Single().To));
    }

    [Fact]
    public async Task Review_ShorterRetentionWithNothingOld_SaysNothingIsDeleted()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        var review = await ReviewAsync(admin, (_days, "3"));

        Assert.False(Assert.Single(review.Notes).Risky);
        Assert.False(review.ConfirmationRequired);
    }

    [Fact]
    public async Task Put_FromAnOlderVersion_IsAConflict()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var version = (await admin.GetFromJsonAsync<SettingsPage>("/api/v1/settings"))!.Version;
        var changes = new[] { new { key = "Veyra:Query:MaxPageSize", value = "500" } };
        (await admin.PutAsJsonAsync("/api/v1/settings", new { version, changes })).EnsureSuccessStatusCode();

        using var late = await admin.PutAsJsonAsync(
            "/api/v1/settings",
            new { version, changes = new[] { new { key = "Veyra:Query:MaxPageSize", value = "800" } } });

        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
    }

    [Fact]
    public async Task Put_Secret_IsNeverSentBack_AndTheHistoryHidesIt()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SaveAsync(admin, (_password, "a long smtp password"));

        var page = await admin.GetFromJsonAsync<SettingsPage>("/api/v1/settings");
        var history = await admin.GetFromJsonAsync<History>("/api/v1/settings/history");

        var password = page!.Settings.Single(setting => setting.Key == _password);
        Assert.Equal(((string?)null, true), (password.Value, password.IsSet));
        var change = Assert.Single(history!.Changes);
        Assert.Equal(
            (true, (string?)null, (string?)null, false),
            (change.Secret, change.OldValue, change.NewValue, change.CanRevert));
        Assert.DoesNotContain(
            "a long smtp password",
            await admin.GetStringAsync("/api/v1/settings/export"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Put_NullValue_ResetsToTheDefault()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SaveAsync(admin, ("Veyra:Query:MaxPageSize", "500"));

        var after = await SaveAsync(admin, ("Veyra:Query:MaxPageSize", null));

        var size = after.Settings.Single(setting => setting.Key == "Veyra:Query:MaxPageSize");
        Assert.Equal(("1000", false), (size.Value, size.Saved));
    }

    [Fact]
    public async Task Revert_AChange_SavesItsOldValueAgain_AsANewChange()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SaveAsync(admin, ("Veyra:Query:MaxPageSize", "500"));
        var page = await SaveAsync(admin, ("Veyra:Query:MaxPageSize", "800"));
        var history = await admin.GetFromJsonAsync<History>("/api/v1/settings/history");

        using var response = await admin.PostAsJsonAsync(
            $"/api/v1/settings/history/{history!.Changes[0].Id}/revert",
            new { version = page.Version });

        response.EnsureSuccessStatusCode();
        var reverted = (await response.Content.ReadFromJsonAsync<SettingsPage>())!;
        Assert.Equal("500", reverted.Settings.Single(setting => setting.Key == "Veyra:Query:MaxPageSize").Value);
        var newest = (await admin.GetFromJsonAsync<History>("/api/v1/settings/history"))!.Changes[0];
        Assert.Equal(("Reverted", "800", "500"), (newest.Kind, newest.OldValue, newest.NewValue));
    }

    [Fact]
    public async Task Import_ExportedFile_ReviewsOnlyWhatWouldChange()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SaveAsync(admin, ("Veyra:Query:MaxPageSize", "500"), ("Veyra:Storage:MetricRetentionDays", "60"));
        var exported = await admin.GetFromJsonAsync<ExportFile>("/api/v1/settings/export");
        await SaveAsync(admin, ("Veyra:Query:MaxPageSize", "800"));

        using var response = await admin.PostAsJsonAsync("/api/v1/settings/import", exported);

        response.EnsureSuccessStatusCode();
        var review = (await response.Content.ReadFromJsonAsync<Review>())!;
        Assert.Equal(
            ("Veyra:Query:MaxPageSize", "800", "500"),
            (review.Changes.Single().Key, review.Changes.Single().From, review.Changes.Single().To));
    }

    private static async Task<Review> ReviewAsync(HttpClient admin, params (string Key, string? Value)[] changes)
    {
        var version = (await admin.GetFromJsonAsync<SettingsPage>("/api/v1/settings"))!.Version;
        using var response = await admin.PostAsJsonAsync(
            "/api/v1/settings/review",
            new { version, changes = changes.Select(change => new { key = change.Key, value = change.Value }) });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Review>())!;
    }

    private static async Task<SettingsPage> SaveAsync(HttpClient admin, params (string Key, string? Value)[] changes)
    {
        var version = (await admin.GetFromJsonAsync<SettingsPage>("/api/v1/settings"))!.Version;
        using var response = await admin.PutAsJsonAsync(
            "/api/v1/settings",
            new
            {
                version,
                changes = changes.Select(change => new { key = change.Key, value = change.Value }),
                confirmed = true,
            });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SettingsPage>())!;
    }

    private static async Task<HttpClient> CreateViewerAsync(VeyraApiFactory factory, HttpClient admin)
    {
        const string password = "another long secret";
        using var created = await admin.PostAsJsonAsync(
            "/api/v1/users",
            new { email = "viewer@example.com", displayName = "Viewer", role = "Viewer", password });
        created.EnsureSuccessStatusCode();

        return await factory.CreateSignedInClientAsync("viewer@example.com", password);
    }

    private sealed record SettingsPage(
        long Version,
        List<Setting> Settings,
        List<SystemItem> System,
        int Rules = 0,
        int RulesOn = 0);

    private sealed record Setting(
        string Key,
        string Section,
        string Label,
        bool Advanced,
        string? Value,
        bool IsSet,
        string? Default,
        Source DefaultSource,
        bool Saved,
        string? SavedBy);

    private sealed record Source(string Kind, string? Name);

    private sealed record SystemItem(string Label, string Value, string Source);

    private sealed record Listing(List<string> Keys, List<string> Sections);

    private sealed record Review(
        List<Problem> Problems,
        List<Note> Notes,
        List<Change> Changes,
        bool ConfirmationRequired);

    private sealed record Problem(string? Key, string Message);

    private sealed record Note(string Key, string Text, bool Risky);

    private sealed record Change(string Key, string? From, string? To);

    private sealed record History(List<HistoryChange> Changes, long? Next);

    private sealed record HistoryChange(
        long Id,
        string Key,
        string? OldValue,
        string? NewValue,
        bool Secret,
        string Kind,
        bool CanRevert);

    private sealed record ExportFile(string Format, int FormatVersion, Dictionary<string, string> Values);
}
