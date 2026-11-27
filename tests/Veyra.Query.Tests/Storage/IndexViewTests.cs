using System.Globalization;
using Veyra.Core.Activity;
using Veyra.Query.Storage;

namespace Veyra.Query.Tests.Storage;

/// <summary>
/// The index list is cut to the largest 100 on the server, so the server's own databases are left out before the cut
/// (VEY-66): otherwise msdb's history tables could take the places of the applications' indexes.
/// </summary>
public class IndexViewTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_SystemHidden_ListsTheApplicationsIndexesBeforeLargerSystemOnes()
    {
        var view = IndexViewBuilder.Build(Rows(), _now, system: false);

        Assert.Equal(["Orders"], view.Indexes.Select(index => index.Table));
        Assert.Equal(105, view.HiddenIndexes.Count);
        Assert.Equal(["msdb"], view.HiddenIndexes.Names);
    }

    [Fact]
    public void Build_SystemShown_ListsTheLargestHundredWhereverTheyAre()
    {
        var view = IndexViewBuilder.Build(Rows(), _now, system: true);

        Assert.Equal(100, view.Indexes.Count);
        Assert.All(view.Indexes, index => Assert.Equal("msdb", index.Database));
        Assert.Equal(0, view.HiddenIndexes.Count);
    }

    [Fact]
    public void Build_Simple_KeepsTheFiveWorstFindingsAndNoList()
    {
        // Eight unused indexes: the Simple view shows the five that cost the most writes (VEY-67).
        var unused = Enumerable.Range(1, 8)
            .Select(index => Usage("shop", $"Table{index}", 1e6, reads: 0, writes: index * 100))
            .ToList();

        var simple = IndexViewBuilder.Build(unused, _now, system: false, simple: true);
        var advanced = IndexViewBuilder.Build(unused, _now, system: false);

        Assert.Equal(5, simple.Findings.Count);
        Assert.Contains("Table8", simple.Findings[0].Title, StringComparison.Ordinal);
        Assert.Empty(simple.Indexes);
        Assert.Equal((8, 8), (advanced.Findings.Count, advanced.Indexes.Count));
    }

    // 105 large indexes in msdb and one small one of the application's.
    private static List<ActivityRecord> Rows() =>
    [
        .. Enumerable.Range(0, 105).Select(index => Usage("msdb", $"sysjobhistory_{index}", 9e9)),
        Usage("shop", "Orders", 1e6),
    ];

    private static ActivityRecord Usage(
        string database,
        string table,
        double size,
        double reads = 5000,
        double writes = 10) =>
        new(
            "microsoft.sql_server sql-prod-01",
            "production",
            _now.AddMinutes(-30),
            IndexViewBuilder.IndexKind,
            $"{database}|{table}|IX_{table}",
            new Dictionary<string, string>
            {
                ["row_type"] = "usage",
                ["database_name"] = database,
                ["table_name"] = table,
                ["index_name"] = $"IX_{table}",
                ["index_type"] = "NONCLUSTERED",
                ["include_columns"] = "",
                ["size_bytes"] = size.ToString(CultureInfo.InvariantCulture),
                ["reads"] = reads.ToString(CultureInfo.InvariantCulture),
                ["writes"] = writes.ToString(CultureInfo.InvariantCulture),
                ["escalations"] = "0",
            },
            "Id");
}
