using Veyra.Core.Schema;

namespace Veyra.Core.Tests.Schema;

public class SchemaMigrationScriptsTests
{
    [Fact]
    public void ParseScriptName_ValidResourceName_ReturnsVersionAndName()
    {
        var (version, name) = SchemaMigrationScripts.ParseScriptName(
            "Veyra.Storage.Migrations.V0001__create_log_record.sql");

        Assert.Equal(1, version);
        Assert.Equal("create_log_record", name);
    }

    [Theory]
    [InlineData("Veyra.Storage.Migrations.V1__too_short.sql")]
    [InlineData("Veyra.Storage.Migrations.V0001_single_underscore.sql")]
    [InlineData("Veyra.Storage.Migrations.V0001__Upper_Case.sql")]
    [InlineData("Veyra.Storage.Migrations.V0000__zero_version.sql")]
    [InlineData("Veyra.Storage.Migrations.readme.txt")]
    public void ParseScriptName_InvalidResourceName_ThrowsFormatException(string resourceName)
    {
        Assert.Throws<FormatException>(() => SchemaMigrationScripts.ParseScriptName(resourceName));
    }

    [Fact]
    public void Create_ScriptsOutOfOrder_ReturnsThemOrderedByVersion()
    {
        var migrations = SchemaMigrationScripts.Create(
            "storage",
            [
                ("Veyra.Storage.Migrations.V0002__second.sql", "SELECT 2;"),
                ("Veyra.Storage.Migrations.V0001__first.sql", "SELECT 1;"),
            ]);

        Assert.Collection(
            migrations,
            first => Assert.Equal(new SchemaMigration("storage", 1, "first", "SELECT 1;"), first),
            second => Assert.Equal(new SchemaMigration("storage", 2, "second", "SELECT 2;"), second));
    }

    [Fact]
    public void Create_DuplicateVersion_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => SchemaMigrationScripts.Create(
            "storage",
            [
                ("Veyra.Storage.Migrations.V0001__first.sql", "SELECT 1;"),
                ("Veyra.Storage.Migrations.V0001__also_first.sql", "SELECT 1;"),
            ]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Storage")]
    [InlineData("data-store")]
    public void Create_InvalidModuleName_ThrowsArgumentException(string module)
    {
        Assert.Throws<ArgumentException>(() => SchemaMigrationScripts.Create(module, []));
    }
}
