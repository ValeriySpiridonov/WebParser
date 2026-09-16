using System.Text;
using WebParser.Config;
using WebParser.Parser;

namespace WebParser.Tests;

public class ContentParserTests
{
    static ContentParserTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static CrawlerOptions Options(int maxAttempts = 2, int timeoutSeconds = 10, long? maxPageSize = null, int perHost = 100) => new()
    {
        MaxConcurrency = 10,
        MaxAttempts = maxAttempts,
        MaxConcurrentPerHost = perHost,
        MaxPageSizeBytes = maxPageSize ?? 5 * 1024 * 1024,
        TimeoutSeconds = timeoutSeconds,
        RetryBaseDelayMs = 5,
        RetryAfterCapSeconds = 5,
        UserAgent = "WebParser.Tests/1.0"
    };

    private static async Task<ParseResult> ParseAsync(ContentParser parser, StubHttpServer server, string path)
        => await parser.ParseAsync(server.BaseUrl + path.TrimStart('/'));

    [Fact]
    public async Task Success_YieldsWordFrequencies()
    {
        using var server = new StubHttpServer(_ => StubHttpServer.Response.Html("<p>Hello World</p><p>Hello</p>"));
        var logger = new TestLogger();
        var parser = new ContentParser(logger, Options());

        ParseResult result = await ParseAsync(parser, server, "/page");

        Assert.True(result.Succeeded);
        Assert.Equal(ParseOutcome.Success, result.Outcome);
        Assert.Equal(new[] { new WordFrequency("hello", 2), new WordFrequency("world", 1) }, result.Words);
    }

    [Fact]
    public async Task CharsetFromHeader_Win1251_Decodet()
    {
        byte[] body = Encoding.GetEncoding(1251).GetBytes("<p>Привет мир</p>");
        using var server = new StubHttpServer(_ => StubHttpServer.Response.Raw(body, "text/html; charset=windows-1251"));
        var parser = new ContentParser(new NullLogger(), Options());

        ParseResult result = await ParseAsync(parser, server, "/page");

        Assert.Equal(new[] { new WordFrequency("мир", 1), new WordFrequency("привет", 1) }, result.Words);
    }

    [Fact]
    public async Task CharsetFromMeta_Utf8_Decodet()
    {
        byte[] body = Encoding.UTF8.GetBytes("<meta charset=\"utf-8\"><p>Тест успешен</p>");
        using var server = new StubHttpServer(_ => StubHttpServer.Response.Raw(body, "text/html"));
        var parser = new ContentParser(new NullLogger(), Options());

        ParseResult result = await ParseAsync(parser, server, "/page");

        Assert.Equal(new[] { new WordFrequency("тест", 1), new WordFrequency("успешен", 1) }, result.Words);
    }

    [Fact]
    public async Task CharsetFromHeader_TakesPriorityOverMeta()
    {
        byte[] body = Encoding.GetEncoding(1251).GetBytes("<meta charset=\"utf-8\"><p>Привет</p>");
        using var server = new StubHttpServer(_ => StubHttpServer.Response.Raw(body, "text/html; charset=windows-1251"));
        var parser = new ContentParser(new NullLogger(), Options());

        ParseResult result = await ParseAsync(parser, server, "/page");

        Assert.Equal(new[] { new WordFrequency("привет", 1) }, result.Words);
    }

    [Fact]
    public async Task PageTooLarge_ReturnsPageTooLarge_SingleRequest()
    {
        string big = new string('a', 4096);
        using var server = new StubHttpServer(_ => StubHttpServer.Response.Html($"<p>{big}</p>"));
        var parser = new ContentParser(new NullLogger(), Options(maxPageSize: 1024));

        ParseResult result = await ParseAsync(parser, server, "/big");

        Assert.Equal(ParseOutcome.PageTooLarge, result.Outcome);
        Assert.Equal(1, server.Hits("/big"));
    }

    [Fact]
    public async Task Retry_OnServiceUnavailable_ThenSuccess()
    {
        int attempts = 0;
        using var server = new StubHttpServer(_ =>
            Interlocked.Increment(ref attempts) == 1
                ? new StubHttpServer.Response { StatusCode = 503 }
                : StubHttpServer.Response.Html("<p>ok</p>"));
        var parser = new ContentParser(new NullLogger(), Options(maxAttempts: 3));

        ParseResult result = await ParseAsync(parser, server, "/retry");

        Assert.True(result.Succeeded);
        Assert.Equal(2, server.Hits("/retry"));
    }

    [Fact]
    public async Task Retry_Exhausted_ReturnsHttpError()
    {
        using var server = new StubHttpServer(_ => new StubHttpServer.Response { StatusCode = 503 });
        var parser = new ContentParser(new NullLogger(), Options(maxAttempts: 2));

        ParseResult result = await ParseAsync(parser, server, "/retry");

        Assert.Equal(ParseOutcome.HttpError, result.Outcome);
        Assert.Equal(2, server.Hits("/retry"));
    }

    [Fact]
    public async Task NotFound_ReturnsHttpError_WithoutRetry()
    {
        using var server = new StubHttpServer(_ => StubHttpServer.Response.Html("<p>nope</p>", statusCode: 404));
        var parser = new ContentParser(new NullLogger(), Options(maxAttempts: 3));

        ParseResult result = await ParseAsync(parser, server, "/missing");

        Assert.Equal(ParseOutcome.HttpError, result.Outcome);
        Assert.Equal(1, server.Hits("/missing"));
    }

    [Fact]
    public async Task InvalidUrl_ReturnsInvalidUrl()
    {
        var parser = new ContentParser(new NullLogger(), Options());

        ParseResult result = await parser.ParseAsync("not a url");

        Assert.Equal(ParseOutcome.InvalidUrl, result.Outcome);
    }

    [Fact]
    public async Task Timeout_YieldsFailed_AfterRetries()
    {
        using var server = new StubHttpServer(_ => new StubHttpServer.Response
        {
            StatusCode = 200,
            ContentType = "text/html",
            Body = Array.Empty<byte>(),
            DelayMs = 3000
        });
        var parser = new ContentParser(new NullLogger(), Options(maxAttempts: 2, timeoutSeconds: 1));

        ParseResult result = await ParseAsync(parser, server, "/slow");

        Assert.Equal(ParseOutcome.Failed, result.Outcome);
        Assert.Equal(2, server.Hits("/slow"));
    }

    [Fact]
    public async Task CallerCancellation_StillThrows()
    {
        using var server = new StubHttpServer(_ => StubHttpServer.Response.Html("<p>x</p>"));
        var parser = new ContentParser(new NullLogger(), Options());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => parser.ParseAsync(server.BaseUrl + "cancel", cts.Token));
    }

    [Fact]
    public async Task CharsetBom_OverridesHeader()
    {
        byte[] body = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("<p>Привет мир</p>"))
            .ToArray();
        using var server = new StubHttpServer(_ => StubHttpServer.Response.Raw(body, "text/html; charset=windows-1251"));
        var parser = new ContentParser(new NullLogger(), Options());

        ParseResult result = await ParseAsync(parser, server, "/bom");

        Assert.Equal(new[] { new WordFrequency("мир", 1), new WordFrequency("привет", 1) }, result.Words);
    }

    [Fact]
    public async Task UnreachableHost_YieldsFailed()
    {
        int leakPort;
        using (var leak = new StubHttpServer(_ => StubHttpServer.Response.Html("<p>x</p>")))
        {
            leakPort = leak.Port;
        }

        var parser = new ContentParser(new NullLogger(), Options(maxAttempts: 2, timeoutSeconds: 5));
        ParseResult result = await parser.ParseAsync($"http://127.0.0.1:{leakPort}/dead");

        Assert.Equal(ParseOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task PerHostLimit_IsRespected()
    {
        using var server = new StubHttpServer(_ => new StubHttpServer.Response
        {
            StatusCode = 200,
            ContentType = "text/html",
            Body = Array.Empty<byte>(),
            DelayMs = 300
        });
        var parser = new ContentParser(new NullLogger(), Options(perHost: 1, timeoutSeconds: 10));

        Task<ParseResult>[] tasks = Enumerable.Range(0, 4)
            .Select(i => parser.ParseAsync(server.BaseUrl + "p" + i))
            .ToArray();
        await Task.WhenAll(tasks);

        Assert.All(tasks, t => Assert.True(t.Result.Succeeded));
        Assert.Equal(1, server.PeakConcurrency);
    }
}