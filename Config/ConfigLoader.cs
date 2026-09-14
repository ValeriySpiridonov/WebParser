using System.Text.Json;

namespace WebParser.Config
{
    public static class ConfigLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new LogLevelConverter() }
        };

        public static AppOptions Load(string? filePath = null)
        {
            AppOptions? options = null;
            string? path = ResolvePath(filePath);
            if (path != null)
            {
                try
                {
                    options = JsonSerializer.Deserialize<AppOptions>(File.ReadAllText(path), JsonOptions);
                }
                catch (JsonException ex)
                {
                    Console.Error.WriteLine($"Некорректный файл конфигурации {path}: {ex.Message}");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"Не удалось прочитать файл конфигурации {path}: {ex.Message}");
                }
            }

            return Sanitize(options ?? AppOptions.Default);
        }

        private static string? ResolvePath(string? filePath)
        {
            string fileName = filePath ?? "appsettings.json";
            if (Path.IsPathRooted(fileName) && File.Exists(fileName))
            {
                return fileName;
            }

            string cwd = Path.Combine(Directory.GetCurrentDirectory(), fileName);
            if (File.Exists(cwd))
            {
                return cwd;
            }

            string baseDir = Path.Combine(AppContext.BaseDirectory, fileName);
            return File.Exists(baseDir) ? baseDir : null;
        }

        private static AppOptions Sanitize(AppOptions options)
        {
            var crawler = options.Crawler;
            var logging = options.Logging;
            return new AppOptions
            {
                Crawler = new CrawlerOptions
                {
                    MaxConcurrency = Math.Max(1, crawler.MaxConcurrency),
                    MaxAttempts = Math.Max(1, crawler.MaxAttempts),
                    MaxConcurrentPerHost = Math.Max(1, crawler.MaxConcurrentPerHost),
                    MaxPageSizeBytes = Math.Max(1024 * 1024, crawler.MaxPageSizeBytes),
                    TimeoutSeconds = Math.Max(1, crawler.TimeoutSeconds),
                    RetryBaseDelayMs = Math.Max(0, crawler.RetryBaseDelayMs),
                    RetryAfterCapSeconds = Math.Max(1, crawler.RetryAfterCapSeconds),
                    UserAgent = string.IsNullOrWhiteSpace(crawler.UserAgent) ? "WebParser/1.0" : crawler.UserAgent.Trim()
                },
                Logging = new LoggingOptions
                {
                    FilePath = string.IsNullOrWhiteSpace(logging.FilePath) ? "log.txt" : logging.FilePath,
                    MaxFileSizeBytes = Math.Max(1024, logging.MaxFileSizeBytes),
                    QueueCapacity = Math.Max(1000, logging.QueueCapacity),
                    MinLevel = logging.MinLevel
                }
            };
        }
    }
}