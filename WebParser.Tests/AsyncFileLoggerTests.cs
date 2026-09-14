using WebParser.Config;
using WebParser.Logger;

namespace WebParser.Tests;

public class AsyncFileLoggerTests
{
    [Fact]
    public void WritesMessages_AndFlushesOnDispose()
    {
        string logPath = TempLogPath();
        var options = new LoggingOptions { FilePath = logPath, MaxFileSizeBytes = 1024 * 1024, QueueCapacity = 1000 };
        var logger = new AsyncFileLogger(options, LogLevel.Verbose);

        logger.Log(new LogMessage { Message = "first" });
        logger.Log(new LogMessage { Message = "second" });
        logger.Log(new LogMessage { Message = "third" });
        logger.Dispose();

        string[] lines = File.ReadAllLines(logPath);

        Assert.Contains(lines, l => l.Contains("first"));
        Assert.Contains(lines, l => l.Contains("second"));
        Assert.Contains(lines, l => l.Contains("third"));
    }

    [Fact]
    public void FiltersOutMessagesBelowMinLevel()
    {
        string logPath = TempLogPath();
        var options = new LoggingOptions { FilePath = logPath, MaxFileSizeBytes = 1024 * 1024, QueueCapacity = 1000 };
        var logger = new AsyncFileLogger(options, LogLevel.Error);

        logger.Log(new LogMessage { Message = "skipped-info" });
        logger.Log(new LogMessage { Message = "kept-error", Level = LogLevel.Error });
        logger.Dispose();

        string content = File.ReadAllText(logPath);

        Assert.DoesNotContain("skipped-info", content);
        Assert.Contains("kept-error", content);
    }

    [Fact]
    public void Rotates_WhenFileGrowsBeyondLimit()
    {
        string logPath = TempLogPath();
        var options = new LoggingOptions { FilePath = logPath, MaxFileSizeBytes = 128, QueueCapacity = 1000 };
        var logger = new AsyncFileLogger(options, LogLevel.Verbose);

        for (int i = 0; i < 15; i++)
        {
            logger.Log(new LogMessage { Message = $"m{i}" });
        }
        logger.Dispose();

        Assert.True(File.Exists(logPath + ".old"));
        string current = File.ReadAllText(logPath);
        string old = File.ReadAllText(logPath + ".old");

        Assert.Contains("m14", current + old);
        Assert.True(old.Length > 0);
        Assert.True(LinesIn(logPath) + LinesIn(logPath + ".old") < 15, "при ротации старые записи уходят в .old");
    }

    [Fact]
    public void InvalidLogPath_DoesNotThrow_AndDegradesSafely()
    {
        string missingDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "log.txt");
        var options = new LoggingOptions { FilePath = missingDir, MaxFileSizeBytes = 1024 * 1024, QueueCapacity = 100 };
        using var errors = new StringWriter();
        var logger = new AsyncFileLogger(options, LogLevel.Verbose, errors);

        logger.Log(new LogMessage { Message = "dropped" });
        logger.Dispose();

        Assert.Contains("Не удалось открыть файл лога", errors.ToString());
    }

    private static int LinesIn(string path)
    {
        return File.ReadAllLines(path).Length;
    }

    private static string TempLogPath()
        => Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.log");
}