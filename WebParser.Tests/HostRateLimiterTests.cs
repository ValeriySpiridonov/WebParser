using WebParser.Config;
using WebParser.Parser;

namespace WebParser.Tests;

public class HostRateLimiterTests
{
    private static CrawlerOptions Options(int perHost = 1) => new()
    {
        MaxConcurrentPerHost = perHost,
        HostIdleSeconds = 0,
        HostPruneThreshold = 1,
        HostPruneIntervalSeconds = 0
    };

    [Fact]
    public async Task ConcurrentAcquires_OnSameHost_AreLimited()
    {
        var limiter = new HostRateLimiter(Options(perHost: 1));
        IDisposable first = await limiter.AcquireAsync("a.com");

        Task<IDisposable> pending = limiter.AcquireAsync("a.com");
        await Task.Delay(100);
        Assert.False(pending.IsCompleted, "второй слот на хост выдаваться не должен");

        first.Dispose();
        using IDisposable second = await pending;
    }

    [Fact]
    public async Task HeldGate_IsNotRemovedByPrune()
    {
        var limiter = new HostRateLimiter(Options(perHost: 1));
        IDisposable held = await limiter.AcquireAsync("a.com");
        await Task.Delay(50);

        using var other = await limiter.AcquireAsync("b.com");

        Task<IDisposable> pending = limiter.AcquireAsync("a.com");
        await Task.Delay(100);
        Assert.False(pending.IsCompleted, "удерживаемый хост не должен вычищаться");

        held.Dispose();
        using IDisposable acquired = await pending;
    }

    [Fact]
    public async Task IdleHosts_ArePruned()
    {
        var limiter = new HostRateLimiter(Options());
        IDisposable gate = await limiter.AcquireAsync("a.com");
        gate.Dispose();
        await Task.Delay(50);

        using var other = await limiter.AcquireAsync("b.com");

        Assert.Equal(1, limiter.HostCount);
    }

    [Fact]
    public async Task CancelledWait_DoesNotWedgeHost()
    {
        var limiter = new HostRateLimiter(Options(perHost: 1));
        IDisposable held = await limiter.AcquireAsync("a.com");

        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => limiter.AcquireAsync("a.com", cts.Token));

        held.Dispose();
        using var again = await limiter.AcquireAsync("a.com");
    }
}
