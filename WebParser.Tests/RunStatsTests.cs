using WebParser.Stats;

namespace WebParser.Tests;

public class RunStatsTests
{
    [Fact]
    public void Snapshot_ReflectsRecords()
    {
        var stats = new RunStats();
        stats.Record(true, TimeSpan.FromMilliseconds(100));
        stats.Record(true, TimeSpan.FromMilliseconds(300));
        stats.Record(false, TimeSpan.FromMilliseconds(50));

        var snap = stats.Snapshot();

        Assert.Equal(2, snap.Processed);
        Assert.Equal(1, snap.Errors);
        Assert.Equal(225, snap.AverageMs, 1);
        Assert.True(snap.Elapsed > TimeSpan.Zero);
        Assert.True(snap.Rps > 0);
    }

    [Fact]
    public void Snapshot_Empty_IsZero()
    {
        var snap = new RunStats().Snapshot();

        Assert.Equal(0, snap.Processed);
        Assert.Equal(0, snap.Errors);
        Assert.Equal(0, snap.AverageMs, 1);
        Assert.Equal(0, snap.Rps, 1);
    }

    [Fact]
    public void ToLine_ContainsRussianSummary()
    {
        var stats = new RunStats();
        stats.Record(true, TimeSpan.FromMilliseconds(100));

        string line = stats.ToLine();

        Assert.Contains("Обработано: 1", line);
        Assert.Contains("Ошибок: 0", line);
        Assert.Contains("RPS:", line);
        Assert.Contains("Среднее:", line);
    }
}