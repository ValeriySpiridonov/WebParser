namespace WebParser.Logger
{
    /// <summary>
    /// Сообщение журнала: текст, уровень важности и опциональное исключение.
    /// </summary>
    public interface ILogMessage
    {
        /// <summary>Текст сообщения.</summary>
        string Message { get; }

        /// <summary>Исключение, связанное с сообщением, либо null.</summary>
        Exception? Exception { get; }

        /// <summary>Уровень важности сообщения.</summary>
        LogLevel Level { get; }
    }
}