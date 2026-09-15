namespace WebParser.Logger
{
    /// <summary>
    /// Реализация сообщения журнала по умолчанию.
    /// </summary>
    public record LogMessage : ILogMessage
    {
        /// <inheritdoc />
        public required string Message { get; init; }

        /// <inheritdoc />
        public Exception? Exception { get; init; }

        /// <inheritdoc />
        public LogLevel Level { get; init; } = LogLevel.Info;
    }
}