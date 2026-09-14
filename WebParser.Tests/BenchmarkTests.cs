using System.Diagnostics;
using WebParser.Config;
using WebParser.Parser;
using Xunit.Abstractions;

namespace WebParser.Tests;

public class BenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public BenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Load500Urls_AllSucceed()
    {
        using var server = new StubHttpServer(PageHandler);
        (int ok, int failed, TimeSpan elapsed, _, _) = await RunLoadAsync(server.BaseUrl, 500, 10);

        _output.WriteLine($"URL=500 elapsed={elapsed.TotalSeconds:F2}s ok={ok} failed={failed}");
        Assert.Equal(500, ok);
        Assert.Equal(0, failed);
        Assert.True(elapsed < TimeSpan.FromSeconds(30), $"Заняло {elapsed}");
    }

    [Fact]
    public async Task Load500000Urls_HighThroughput_StableMemory()
    {
        using var server = new StubHttpServer(PageHandler);
        (int ok, int failed, TimeSpan elapsed, long startBytes, long endBytes) = await RunLoadAsync(server.BaseUrl, 500000, 10);

        double rps = ok / elapsed.TotalSeconds;
        double avgMs = elapsed.TotalMilliseconds / 500000;
        long deltaKb = (endBytes - startBytes) / 1024;
        _output.WriteLine($"URL=500000 elapsed={elapsed.TotalSeconds:F2}s RPS={rps:F1} avg={avgMs:F1}ms memBefore={startBytes / 1024}KB memAfter={endBytes / 1024}KB delta={deltaKb}KB");

        Assert.True(ok * 100 >= 500000 * 99, $"Успешно {ok} из 500000");
        Assert.Equal(500000 - ok, failed);
        Assert.True(elapsed < TimeSpan.FromSeconds(2 + 500000 / 2000.0), $"Заняло {elapsed}");
        Assert.True(endBytes - startBytes < 512L * 1024 * 1024, $"Рост памяти {deltaKb}KB");
    }

    private static StubHttpServer.Response PageHandler(string path) =>
        StubHttpServer.Response.Html($"<p>Page {path} content here</p>");

    private static async Task<(int Ok, int Failed, TimeSpan Elapsed, long StartBytes, long EndBytes)> RunLoadAsync(
        string baseUrl, int count, int maxConcurrency)
    {
        using var parser = new ContentParser(new NullLogger(), new CrawlerOptions
        {
            MaxConcurrency = maxConcurrency,
            MaxAttempts = 2,
            MaxConcurrentPerHost = 100,
            MaxPageSizeBytes = 1024 * 1024,
            TimeoutSeconds = 10,
            RetryBaseDelayMs = 5
        });
        string[] urls = Enumerable.Range(0, count).Select(i => baseUrl + "p" + i).ToArray();

        int ok = 0;
        int failed = 0;
        long startBytes = GC.GetTotalMemory(forceFullCollection: true);
        var sw = Stopwatch.StartNew();
        await Parallel.ForEachAsync(urls, new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency }, async (url, ct) =>
        {
            ParseResult result = await parser.ParseAsync(url, ct);
            if (result.Succeeded)
            {
                Interlocked.Increment(ref ok);
            }
            else
            {
                Interlocked.Increment(ref failed);
            }
        });
        sw.Stop();
        long endBytes = GC.GetTotalMemory(forceFullCollection: true);

        return (ok, failed, sw.Elapsed, startBytes, endBytes);
    }
}