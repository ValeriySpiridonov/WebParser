using WebParser.Logger;

namespace WebParser.Config
{
    public sealed class AppOptions
    {
        public CrawlerOptions Crawler { get; init; } = new();
        public LoggingOptions Logging { get; init; } = new();

        public static AppOptions Default { get; } = new();
    }

    public sealed class CrawlerOptions
    {
        public int MaxConcurrency { get; init; } = 10;
        public int MaxAttempts { get; init; } = 3;
        public int MaxConcurrentPerHost { get; init; } = 2;
        public long MaxPageSizeBytes { get; init; } = 5 * 1024 * 1024;
        public int TimeoutSeconds { get; init; } = 30;
        public int RetryBaseDelayMs { get; init; } = 500;
        public int RetryAfterCapSeconds { get; init; } = 30;
        public string UserAgent { get; init; } = "WebParser/1.0";
    }

    public sealed class LoggingOptions
    {
        public string FilePath { get; init; } = "log.txt";
        public long MaxFileSizeBytes { get; init; } = 8 * 1024 * 1024;
        public int QueueCapacity { get; init; } = 10000;
        public LogLevel MinLevel { get; init; } = LogLevel.Info;
    }
}