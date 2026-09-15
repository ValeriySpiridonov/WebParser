using WebParser.Logger;

namespace WebParser.Config
{
    /// <summary>
    /// Параметры логирования: файл, ротация и минимальный уровень.
    /// </summary>
    public sealed class LoggingOptions
    {
        /// <summary>Путь к файлу журнала.</summary>
        public string FilePath { get; init; } = "log.txt";

        /// <summary>Максимальный размер файла журнала в байтах, после чего выполняется ротация.</summary>
        public long MaxFileSizeBytes { get; init; } = 8 * 1024 * 1024;

        /// <summary>Вместимость очереди сообщений логгера.</summary>
        public int QueueCapacity { get; init; } = 10000;

        /// <summary>Минимальный уровень записываемых сообщений.</summary>
        public LogLevel MinLevel { get; init; } = LogLevel.Info;
    }
}