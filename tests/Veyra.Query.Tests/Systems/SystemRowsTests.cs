using Veyra.Query.Systems;

namespace Veyra.Query.Tests.Systems;

/// <summary>
/// The server leaves out a database server's own rows unless asked (VEY-66), and says how many it left out and which of
/// them an alert could name, so the screen never has to filter what it receives.
/// </summary>
public class SystemRowsTests
{
    private static readonly (string Name, bool System)[] _rows =
    [
        ("shop", false),
        ("msdb", true),
        ("reporting", false),
        ("MSDB", true),
        ("master", true),
    ];

    [Fact]
    public void Keep_SystemHidden_KeepsTheApplicationsRowsInOrderAndCountsTheRest()
    {
        var (rows, hidden) = SystemRows.Keep(_rows, shown: false, row => row.System, row => row.Name);

        Assert.Equal(["shop", "reporting"], rows.Select(row => row.Name));
        Assert.Equal(3, hidden.Count);
        Assert.Equal(["master", "msdb"], hidden.Names);
    }

    [Fact]
    public void Keep_SystemShown_KeepsEveryRowAndHidesNothing()
    {
        var (rows, hidden) = SystemRows.Keep(_rows, shown: true, row => row.System, row => row.Name);

        Assert.Equal(5, rows.Count);
        Assert.Equal(0, hidden.Count);
        Assert.Empty(hidden.Names);
    }

    [Fact]
    public void Keep_RowsNoAlertCanName_AreCountedWithoutAName()
    {
        var (_, hidden) = SystemRows.Keep(_rows, shown: false, row => row.System, _ => null);

        Assert.Equal(3, hidden.Count);
        Assert.Empty(hidden.Names);
    }
}
