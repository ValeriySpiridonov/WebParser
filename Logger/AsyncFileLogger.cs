using System.Text;
using System.Threading.Channels;
using WebParser.Config;

namespace WebParser.Logger
{
    /// <summary>
    /// Асинхронный логгер в файл: очередь сообщений, ротация по размеру, фильтрация по уровню.
    /// </summary>
    public sealed class AsyncFileLogger : ILogger, IDisposable
    {
        private const int FlushMessageInterval = 200;

        private readonly Channel<ILogMessage> _channel;
        private readonly string _logFileName;
        private readonly long _maxLogBytes;
        private readonly LogLevel _minLevel;
        private readonly Task _writerTask;
        private readonly TextWriter _errorSink;

        private StreamWriter _writer;
        private long _bytesWritten;
        private long _messagesSinceFlush;
        private volatile bool _disposed;
        private volatile bool _writerFaulted;

        /// <summary>
        /// Создаёт асинхронный логгер и запускает фоновый цикл записи.
        /// </summary>
        /// <param name="options">Параметры логирования: путь, лимит размера, вместимость очереди.</param>
        /// <param name="minLevel">Минимальный уровень записываемых сообщений.</param>
        /// <param name="errorSink">Поток для вывода ошибок логгера; по умолчанию — без вывода.</param>
        public AsyncFileLogger(LoggingOptions options, LogLevel minLevel, TextWriter? errorSink = null)
        {
            _minLevel = minLevel;
            _maxLogBytes = options.MaxFileSizeBytes;
            _logFileName = ResolveLogPath(options.FilePath);
            _errorSink = errorSink ?? TextWriter.Null;

            try
            {
                _writer = new StreamWriter(_logFileName, append: true, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _writerFaulted = true;
                _errorSink.WriteLine($"Не удалось открыть файл лога {_logFileName}: {ex.Message}");
                _writer = new StreamWriter(Stream.Null);
            }

            _channel = Channel.CreateBounded<ILogMessage>(new BoundedChannelOptions(options.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
            _writerTask = Task.Run(WriteLoopAsync);
        }

        /// <summary>
        /// Ставит сообщение в очередь записи, если его уровень проходит фильтр.
        /// </summary>
        /// <param name="logMessage">Сообщение для записи.</param>
        public void Log(ILogMessage logMessage)
        {
            if (_disposed || _writerFaulted) return;
            if (logMessage.Level < _minLevel) return;

            if (!_channel.Writer.TryWrite(logMessage))
            {
                try
                {
                    _channel.Writer.WriteAsync(logMessage).AsTask().GetAwaiter().GetResult();
                }
                catch (ChannelClosedException)
                {
                    // Канал закрыт (Dispose или сбой писателя) — сообщение намеренно теряется.
                }
            }
        }

        /// <summary>
        /// Завершает запись: закрывает очередь, ожидает выгрузку сообщений и освобождает файл.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _channel.Writer.TryComplete();
            _writerTask.GetAwaiter().GetResult();
            try
            {
                _writer.Dispose();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _errorSink.WriteLine($"Не удалось закрыть файл лога {_logFileName}: {ex.Message}");
            }
        }

        private async Task WriteLoopAsync()
        {
            try
            {
                await foreach (ILogMessage logMessage in _channel.Reader.ReadAllAsync())
                {
                    WriteToFile(logMessage);

                    if (_bytesWritten >= _maxLogBytes)
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
                _channel.Writer.TryComplete();
                _errorSink.WriteLine($"Не удалось записать в лог: {ex.Message}");
            }
        }

        private void WriteToFile(ILogMessage logMessage)
        {
            Span<char> stamp = stackalloc char[32];
            DateTime.Now.TryFormat(stamp, out int written, "yyyy-MM-dd HH:mm:ss");
            _writer.Write(stamp.Slice(0, written));
            _writer.Write(": ");
            _writer.Write(logMessage.Message);

            long bytes = Encoding.UTF8.GetByteCount(stamp.Slice(0, written)) + 3 +
                         Encoding.UTF8.GetByteCount(logMessage.Message.AsSpan());
            if (logMessage.Exception != null)
            {
                _writer.Write(Environment.NewLine);
                _writer.Write("Exception: ");
                _writer.Write(logMessage.Exception);
                bytes += Encoding.UTF8.GetByteCount(Environment.NewLine) + 11 +
                         Encoding.UTF8.GetByteCount(logMessage.Exception.ToString().AsSpan());
            }

            _writer.Write(Environment.NewLine);
            _bytesWritten += bytes + Encoding.UTF8.GetByteCount(Environment.NewLine);
            _messagesSinceFlush++;
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

        private static string ResolveLogPath(string filePath)
        {
            return Path.IsPathRooted(filePath) ? filePath : Path.Combine(AppContext.BaseDirectory, filePath);
        }
    }
}