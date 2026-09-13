using System;
using System.IO;
using System.Reflection;

namespace WebParser.Logger
{
    internal sealed class DisposableFileLogger : ILogger, IDisposable
    {
        private readonly object _lock = new object();
        private readonly StreamWriter _streamWriter;
        private bool _disposed;

        public DisposableFileLogger()
        {
            _streamWriter = new StreamWriter(GetLogFilePath(), true) { AutoFlush = true };
        }

        public void Log(ILogMessage logMessage)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            lock (_lock)
            {
                _streamWriter.WriteLine($"{DateTime.Now}: {logMessage.Message}");
                if (logMessage.Exception != null)
                {
                    _streamWriter.WriteLine($"Exception: {logMessage.Exception}");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _streamWriter.Dispose();
        }

        private static string GetLogFilePath()
        {
            string? baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(baseDirectory))
                baseDirectory = AppContext.BaseDirectory;

            return Path.Combine(baseDirectory, "log.txt");
        }
    }
}