namespace WebParser.Logger
{
    public record LogMessage : ILogMessage
    {
        public required string Message { get; init; }
        public Exception? Exception { get; init; }
        public LogLevel Level { get; init; } = LogLevel.Info;
    }
}