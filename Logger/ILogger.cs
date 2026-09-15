namespace WebParser.Logger
{
    /// <summary>
    /// Интерфейс журнала: приём сообщений для записи.
    /// </summary>
    public interface ILogger
    {
        /// <summary>Записывает сообщение в журнал.</summary>
        /// <param name="logMessage">Сообщение для записи.</param>
        void Log(ILogMessage logMessage);
    }
}