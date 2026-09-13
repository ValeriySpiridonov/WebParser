using System;

namespace WebParser.Logger
{
    public interface ILogMessage
    {
        string Message { get; }
        Exception? Exception { get; }
    }
}