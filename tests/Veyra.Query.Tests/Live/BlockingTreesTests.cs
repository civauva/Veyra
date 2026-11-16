using Veyra.Query.Live;
using static Veyra.Query.Tests.Live.LiveSamples;

namespace Veyra.Query.Tests.Live;

/// <summary>
/// The Live tab draws each blocking chain from its head blocker (ADR 0020). These tests prove the head is the session
/// that waits for nobody, even asleep, and that a blocker that ended or a circle still places every waiting session
/// once.
/// </summary>
public class BlockingTreesTests
{
    [Fact]
    public void Build_Chain_HeadsItWithTheSleepingBlockerAndCountsEveryoneBehind()
    {
        var tree = Assert.Single(BlockingTrees.Build(Chain()));

        Assert.Equal(88, tree.Session);
        Assert.Equal(3, tree.Waiting);
        var update = Assert.Single(tree.Children);
        Assert.Equal(117, update.Session);
        Assert.Equal([131, 140], update.Children.Select(child => child.Session));
        Assert.Equal(
            new Dictionary<int, int> { [88] = 3, [117] = 2 },
            BlockingTrees.WaitingCounts(BlockingTrees.Build(Chain())));
    }

    [Fact]
    public void Build_NobodyWaits_HasNoChains()
    {
        Assert.Empty(BlockingTrees.Build([Running(203, 41_000), Idle(88)]));
    }

    [Fact]
    public void Build_BlockerTheReadDoesNotHold_HeadsTheChainByItsId()
    {
        var tree = Assert.Single(BlockingTrees.Build([Waiting(117, 64, "LCK_M_U", 5_000)]));

        Assert.Equal(64, tree.Session);
        Assert.Equal(117, Assert.Single(tree.Children).Session);
    }

    [Fact]
    public void Build_SessionsWaitingInACircle_PlacesEachOnceUnderTheLowestId()
    {
        var tree = Assert.Single(BlockingTrees.Build(
        [
            Waiting(70, 71, "LCK_M_X", 2_000),
            Waiting(71, 70, "LCK_M_X", 2_000),
        ]));

        Assert.Equal(70, tree.Session);
        Assert.Equal(71, Assert.Single(tree.Children).Session);
        Assert.Empty(tree.Children[0].Children);
    }
}
