using WebParser.Config;
using WebParser.Logger;

namespace WebParser.Tests;

public class ConfigLoaderTests
{
    private static string TempFile(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void MissingFile_YieldsDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.missing");

        AppOptions options = ConfigLoader.Load(path);

        Assert.Equal(10, options.Crawler.MaxConcurrency);
        Assert.Equal(3, options.Crawler.MaxAttempts);
        Assert.Equal("WebParser/1.0", options.Crawler.UserAgent);
        Assert.Equal(LogLevel.Info, options.Logging.MinLevel);
        Assert.Equal("log.txt", options.Logging.FilePath);
    }

    [Fact]
    public void InvalidJson_YieldsDefaults()
    {
        string path = TempFile("{ {{{ not json");

        AppOptions options = ConfigLoader.Load(path);

        Assert.Equal(10, options.Crawler.MaxConcurrency);
    }

    [Fact]
    public void ValidJson_OverridesDefaults()
    {
        string path = TempFile("""
            {
              "crawler": {
                "maxConcurrency": 4,
                "maxAttempts": 5,
                "timeoutSeconds": 12,
                "userAgent": "MyBot/2.0"
              },
              "logging": {
                "minLevel": "Error"
              }
            }
            """);

        AppOptions options = ConfigLoader.Load(path);

        Assert.Equal(4, options.Crawler.MaxConcurrency);
        Assert.Equal(5, options.Crawler.MaxAttempts);
        Assert.Equal(12, options.Crawler.TimeoutSeconds);
        Assert.Equal("MyBot/2.0", options.Crawler.UserAgent);
        Assert.Equal(LogLevel.Error, options.Logging.MinLevel);
    }

    [Fact]
    public void CommentsAndTrailingCommas_AreAccepted()
    {
        string path = TempFile("""
            {
              // комментарий
              "crawler": {
                "maxConcurrency": 7,
              },
            }
            """);

        AppOptions options = ConfigLoader.Load(path);

        Assert.Equal(7, options.Crawler.MaxConcurrency);
    }

    [Fact]
    public void OutOfRangeValues_AreClamped()
    {
        string path = TempFile("""
            {
              "crawler": {
                "maxConcurrency": 0,
                "maxPageSizeBytes": 16,
                "maxAttempts": -3
              },
              "logging": {
                "queueCapacity": 1,
                "maxFileSizeBytes": 10
              }
            }
            """);

        AppOptions options = ConfigLoader.Load(path);

        Assert.Equal(1, options.Crawler.MaxConcurrency);
        Assert.Equal(1, options.Crawler.MaxAttempts);
        Assert.Equal(1024 * 1024, options.Crawler.MaxPageSizeBytes);
        Assert.Equal(1000, options.Logging.QueueCapacity);
        Assert.Equal(1024, options.Logging.MaxFileSizeBytes);
    }
}