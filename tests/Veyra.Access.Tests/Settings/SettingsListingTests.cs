using Veyra.Access.Settings;
using Veyra.Core.Settings;

namespace Veyra.Access.Tests.Settings;

/// <summary>The server decides which settings and sections the Settings screen lists (VEY-67).</summary>
public class SettingsListingTests
{
    private static readonly SettingDefinition _logs = Define(
        "Veyra:Storage:LogRetentionDays",
        SettingSection.Data,
        "Logs",
        "Keep",
        "How many days log records stay.");

    private static readonly SettingDefinition _logRoom = Define(
        "Veyra:Storage:LogMaxBytes",
        SettingSection.Data,
        "Logs",
        "Room",
        "The most disk space logs take.",
        advanced: true);

    private static readonly SettingDefinition _batch = Define(
        "Veyra:Ingestion:BatchSize",
        SettingSection.Ingestion,
        "Writing",
        "Batch",
        "How many records go in one write.",
        advanced: true);

    private static readonly SettingDefinition _smtp = Define(
        "Veyra:Email:Host",
        SettingSection.Email,
        "Server",
        "Host",
        "The mail server that sends messages.");

    private static readonly SettingDefinition[] _all = [_logs, _logRoom, _batch, _smtp];

    [Fact]
    public void Build_SimpleSection_ListsItsBasicsOnly()
    {
        var listing = SettingsListing.Build(_all, null, "Data", advanced: false);

        Assert.Equal([_logs.Key], listing.Keys);
    }

    [Fact]
    public void Build_AdvancedSection_ListsEverySettingOfIt()
    {
        var listing = SettingsListing.Build(_all, null, "data", advanced: true);

        Assert.Equal([_logs.Key, _logRoom.Key], listing.Keys);
    }

    [Fact]
    public void Build_SimpleSectionWithoutBasics_ListsItsAdvancedSettings()
    {
        var listing = SettingsListing.Build(_all, null, "Ingestion", advanced: false);

        Assert.Equal([_batch.Key], listing.Keys);
    }

    [Fact]
    public void Build_Search_FindsEveryWordInAnySectionAndView()
    {
        var listing = SettingsListing.Build(_all, "  disk LOGS ", "Email", advanced: false);

        Assert.Equal([_logRoom.Key], listing.Keys);
    }

    [Fact]
    public void Build_Search_MatchesTheKey()
    {
        var listing = SettingsListing.Build(_all, "batchsize", null, advanced: false);

        Assert.Equal([_batch.Key], listing.Keys);
    }

    [Fact]
    public void Build_Simple_ListsSectionsWithBasicsAndThePagesButNotSystem()
    {
        var listing = SettingsListing.Build(_all, null, null, advanced: false);

        Assert.Equal(["Data", "Email", "Databases", "Agents"], listing.Sections);
    }

    [Fact]
    public void Build_Advanced_ListsEverySectionWithSettingsThePagesAndSystem()
    {
        var listing = SettingsListing.Build(_all, null, null, advanced: true);

        Assert.Equal(["Data", "Email", "Ingestion", "Databases", "Agents", "System"], listing.Sections);
    }

    private static SettingDefinition Define(
        string key,
        SettingSection section,
        string group,
        string label,
        string help,
        bool advanced = false) =>
        new(key, section, group, label, help, SettingKind.WholeNumber, "1") { Advanced = advanced };
}
