namespace WebParser.Config
{
    /// <summary>
    /// Параметры краулера: параллельность, повторы, лимиты и заголовки HTTP.
    /// </summary>
    public sealed class CrawlerOptions
    {
        /// <summary>Максимальное число одновременно обрабатываемых адресов.</summary>
        public int MaxConcurrency { get; init; } = 10;

        /// <summary>Максимальное число попыток запроса к одному адресу.</summary>
        public int MaxAttempts { get; init; } = 3;

        /// <summary>Максимальное число одновременных запросов к одному хосту.</summary>
        public int MaxConcurrentPerHost { get; init; } = 2;

        /// <summary>Через сколько секунд простоя хост вычищается из лимитера одновременных операций.</summary>
        public int HostIdleSeconds { get; init; } = 600;

        /// <summary>Минимальное число хостов в лимитере, при котором начинается вычистка простаивающих.</summary>
        public int HostPruneThreshold { get; init; } = 5000;

        /// <summary>Интервал в секундах между попытками вычистки простаивающих хостов.</summary>
        public int HostPruneIntervalSeconds { get; init; } = 30;

        /// <summary>Максимальный размер скачиваемой страницы в байтах.</summary>
        public long MaxPageSizeBytes { get; init; } = 5 * 1024 * 1024;

        /// <summary>Таймаут одного HTTP-запроса в секундах.</summary>
        public int TimeoutSeconds { get; init; } = 30;

        /// <summary>Базовая задержка перед повтором запроса в миллисекундах.</summary>
        public int RetryBaseDelayMs { get; init; } = 500;

        /// <summary>Верхняя граница задержки по заголовку Retry-After в секундах.</summary>
        public int RetryAfterCapSeconds { get; init; } = 30;

        /// <summary>Значение заголовка User-Agent в запросах.</summary>
        public string UserAgent { get; init; } = "WebParser/1.0";
    }
}