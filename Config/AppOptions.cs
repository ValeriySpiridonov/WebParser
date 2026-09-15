namespace WebParser.Config
{
    /// <summary>
    /// Корневые параметры приложения: краулер и логирование.
    /// </summary>
    public sealed class AppOptions
    {
        /// <summary>Параметры краулера (скачивание и обработка страниц).</summary>
        public CrawlerOptions Crawler { get; init; } = new();

        /// <summary>Параметры логирования.</summary>
        public LoggingOptions Logging { get; init; } = new();

        /// <summary>Вариант конфигурации по умолчанию.</summary>
        public static AppOptions Default { get; } = new();
    }
}