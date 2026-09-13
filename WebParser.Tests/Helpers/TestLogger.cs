using WebParser.Logger;

namespace WebParser.Tests;

internal sealed class TestLogger : ILogger
{
    private readonly List<ILogMessage> _messages = new();
    private readonly object _gate = new();

    public IReadOnlyList<ILogMessage> Messages
    {
        get
        {
            lock (_gate)
            {
                return _messages.ToArray();
            }
        }
    }

    public void Log(ILogMessage logMessage)
    {
        lock (_gate)
        {
            _messages.Add(logMessage);
        }
    }
}

internal sealed class NullLogger : ILogger
{
    public void Log(ILogMessage logMessage)
    {
    }
}