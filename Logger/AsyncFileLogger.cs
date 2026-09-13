using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace WebParser.Logger
{
    public sealed class AsyncFileLogger : ILogger, IDisposable
    {
        private const int QueueCapacity = 10000;
        private const long MaxLogBytes = 8 * 1024 * 1024;
        private const int FlushMessageInterval = 200;

        private readonly Channel<string> _channel;
        private readonly string _logFileName;
        private readonly LogLevel _minLevel;
        private readonly Task _writerTask;

        private StreamWriter _writer;
        private long _bytesWritten;
        private long _messagesSinceFlush;
        private bool _disposed;
        private volatile bool _writerFaulted;

        public AsyncFileLogger(LogLevel minLevel = LogLevel.Info)
        {
            _minLevel = minLevel;
            _logFileName = GetLogFilePath();
            _writer = new StreamWriter(_logFileName, append: true, Encoding.UTF8);
            _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
            _writerTask = Task.Run(WriteLoopAsync);
        }

        public void Log(ILogMessage logMessage)
        {
            if (_disposed || _writerFaulted) return;
            if (logMessage.Level < _minLevel) return;

            string text = Format(logMessage);
            _channel.Writer.TryWrite(text);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _channel.Writer.TryComplete();
            try
            {
                _writerTask.GetAwaiter().GetResult();
            }
            catch
            {
            }
            _writer.Dispose();
        }

        private static string Format(ILogMessage logMessage)
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now).Append(": ").Append(logMessage.Message);
            if (logMessage.Exception != null)
            {
                sb.AppendLine().Append("Exception: ").Append(logMessage.Exception);
            }
            return sb.ToString();
        }

        private async Task WriteLoopAsync()
        {
            try
            {
                await foreach (string line in _channel.Reader.ReadAllAsync())
                {
                    _writer.WriteLine(line);
                    _bytesWritten += Encoding.UTF8.GetByteCount(line.AsSpan()) + 2;
                    _messagesSinceFlush++;

                    if (_bytesWritten >= MaxLogBytes)
                    {
                        Rotate();
                    }
                    else if (_messagesSinceFlush >= FlushMessageInterval)
                    {
                        _writer.Flush();
                        _messagesSinceFlush = 0;
                    }
                }

                _writer.Flush();
            }
            catch (Exception ex)
            {
                _writerFaulted = true;
                Console.Error.WriteLine($"Не удалось записать в лог: {ex.Message}");
            }
        }

        private void Rotate()
        {
            _writer.Flush();
            _writer.Dispose();
            File.Move(_logFileName, _logFileName + ".old", overwrite: true);
            _writer = new StreamWriter(_logFileName, append: false, Encoding.UTF8);
            _bytesWritten = 0;
            _messagesSinceFlush = 0;
        }

        private static string GetLogFilePath()
        {
            return Path.Combine(AppContext.BaseDirectory, "log.txt");
        }
    }
}