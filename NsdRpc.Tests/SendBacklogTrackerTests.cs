using NsdRpc.Tcp;

namespace NsdRpc.Tests;

public sealed class SendBacklogTrackerTests
{
    private const int Threshold = 1024;

    private const int WindowMs = 1000;

    [Fact]
    public void BacklogBelowTheThresholdNeverCloses()
    {
        var tracker = new SendBacklogTracker(Threshold, WindowMs);

        Assert.False(tracker.ExceedsWindow(Threshold - 1, 0L));
        Assert.False(tracker.ExceedsWindow(Threshold - 1, 2_000_000_000L));
    }

    [Fact]
    public void BacklogClosesAfterTheWholeWindow()
    {
        var tracker = new SendBacklogTracker(Threshold, WindowMs);

        Assert.False(tracker.ExceedsWindow(Threshold, 0L));
        Assert.False(tracker.ExceedsWindow(Threshold, 999_000_000L));
        Assert.True(tracker.ExceedsWindow(Threshold, 1_000_000_000L));
    }

    [Fact]
    public void BacklogDropResetsTheWindow()
    {
        var tracker = new SendBacklogTracker(Threshold, WindowMs);

        Assert.False(tracker.ExceedsWindow(Threshold, 0L));
        Assert.False(tracker.ExceedsWindow(Threshold - 1, 500_000_000L));
        Assert.False(tracker.ExceedsWindow(Threshold, 600_000_000L));
        Assert.False(tracker.ExceedsWindow(Threshold, 1_599_000_000L));
        Assert.True(tracker.ExceedsWindow(Threshold, 1_600_000_000L));
    }

    [Fact]
    public void DisabledTrackerNeverCloses()
    {
        var tracker = new SendBacklogTracker(0, 0);

        Assert.False(tracker.ExceedsWindow(int.MaxValue, long.MaxValue));
    }
}
