using Veyra.Core.Telemetry;
using Veyra.Query.Home;

namespace Veyra.Query.Tests.Home;

public class LogStorageOutlookTests
{
    private const long _gigabyte = 1024L * 1024 * 1024;

    [Fact]
    public void From_NoBudget_DoesNotKnowWhetherABudgetIsReached()
    {
        var storage = LogStorageOutlook.From(new LogStorageUsage(_gigabyte, null, 14, _gigabyte));

        Assert.Null(storage.ReachesBudget);
        Assert.Null(storage.DaysUntilBudget);
    }

    [Fact]
    public void From_NoFullDayYet_IsStillLearning()
    {
        var storage = LogStorageOutlook.From(new LogStorageUsage(_gigabyte, 20 * _gigabyte, 14, null));

        Assert.Null(storage.ReachesBudget);
        Assert.Null(storage.DaysUntilBudget);
    }

    [Fact]
    public void From_RetentionKeepsStorageUnderBudget_NeverReachesIt()
    {
        // 14 days plus today at 1 GB a day is 15 GB, under the 20 GB budget.
        var storage = LogStorageOutlook.From(new LogStorageUsage(3 * _gigabyte, 20 * _gigabyte, 14, _gigabyte));

        Assert.False(storage.ReachesBudget);
        Assert.Null(storage.DaysUntilBudget);
    }

    [Fact]
    public void From_GrowthOutrunsRetention_CountsTheDaysLeft()
    {
        var storage = LogStorageOutlook.From(new LogStorageUsage(3 * _gigabyte, 20 * _gigabyte, 30, 2 * _gigabyte));

        // 17 GB left at 2 GB a day: the budget is reached during the ninth day.
        Assert.True(storage.ReachesBudget);
        Assert.Equal(9, storage.DaysUntilBudget);
    }

    [Fact]
    public void From_AlreadyOverBudget_IsZeroDays()
    {
        var storage = LogStorageOutlook.From(new LogStorageUsage(25 * _gigabyte, 20 * _gigabyte, 30, _gigabyte));

        Assert.True(storage.ReachesBudget);
        Assert.Equal(0, storage.DaysUntilBudget);
    }
}
