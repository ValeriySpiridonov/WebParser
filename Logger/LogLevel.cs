namespace WebParser.Logger
{
    /// <summary>
    /// Уровни важности сообщений журнала.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>Подробные сообщения (например, список слов по адресам).</summary>
        Verbose = 0,

        /// <summary>Обычные информационные сообщения.</summary>
        Info = 1,

        /// <summary>Сообщения об ошибках.</summary>
        Error = 2
    }
}