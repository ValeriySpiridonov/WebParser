using System;

namespace WebParser.Logger
{
    public enum LogLevel
    {
        Verbose = 0,
        Info = 1,
        Error = 2
    }

    public interface ILogMessage
    {
        string Message { get; }
        Exception? Exception { get; }
        LogLevel Level { get; }
    }
}