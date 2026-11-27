using Veyra.Query.Queries.Api;
using Veyra.Query.Storage;

namespace Veyra.Query.Tests.Storage;

/// <summary>The Storage tab's findings come merged and ranked from the server (VEY-68).</summary>
public class StorageViewTests
{
    [Fact]
    public void NeedsDoing_ServerAndDatabaseFindings_AreOneListTheWarningsFirstEachWithItsDatabase()
    {
        PlanWarningResponse[] server = [Finding("volume", "caution")];
        (string Database, PlanWarningResponse Finding)[] databases =
        [
            ("shop", Finding("auto-shrink", "caution")),
            ("shop", Finding("no-backup", "warning")),
            ("reporting", Finding("log-full", "warning")),
        ];

        var list = StorageViewBuilder.NeedsDoing(server, databases);

        Assert.Equal(
            [("no-backup", "shop"), ("log-full", "reporting"), ("volume", null), ("auto-shrink", "shop")],
            list.Select(item => (item.Finding.Kind, item.Database)));
    }

    private static PlanWarningResponse Finding(string kind, string severity) =>
        new(kind, severity, kind, "", "", null);
}
