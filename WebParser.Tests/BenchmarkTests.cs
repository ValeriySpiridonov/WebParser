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
    public async Task Load500Urls_Completes_WithStableMemory()
    {
        using var server = new StubHttpServer(path => StubHttpServer.Response.Html($"<p>Page {path} content here</p>"));
        var options = new CrawlerOptions
        {
            MaxConcurrency = 10,
            MaxAttempts = 2,
            MaxConcurrentPerHost = 100,
            MaxPageSizeBytes = 1024 * 1024,
            TimeoutSeconds = 10,
            RetryBaseDelayMs = 5
        };
        var parser = new ContentParser(new NullLogger(), options);
        string[] urls = Enumerable.Range(0, 500).Select(i => server.BaseUrl + "p" + i).ToArray();

        long startMem = GC.GetTotalMemory(forceFullCollection: true);
        var sw = Stopwatch.StartNew();
        int ok = 0;
        int failed = 0;
        await Parallel.ForEachAsync(urls, new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (url, ct) =>
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
        long endMem = GC.GetTotalMemory(forceFullCollection: true);

        double rps = ok / sw.Elapsed.TotalSeconds;
        double avgMs = sw.Elapsed.TotalMilliseconds / urls.Length;
        long deltaKb = (endMem - startMem) / 1024;
        _output.WriteLine($"URL={urls.Length} elapsed={sw.Elapsed.TotalSeconds:F2}s RPS={rps:F1} avg={avgMs:F1}ms memBefore={startMem / 1024}KB memAfter={endMem / 1024}KB delta={deltaKb}KB");

        Assert.Equal(500, ok);
        Assert.Equal(0, failed);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"Заняло {sw.Elapsed}");
        Assert.True(endMem - startMem < 512L * 1024 * 1024, $"Рост памяти {deltaKb}KB");
    }
}