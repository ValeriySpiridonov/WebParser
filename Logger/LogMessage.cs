using System;

namespace WebParser.Logger
{
    public record LogMessage : ILogMessage
    {
        public string Message { get; init; }
        public Exception Exception { get; init; }
    }
}