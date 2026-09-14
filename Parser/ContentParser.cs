using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using WebParser.Config;
using WebParser.Logger;

namespace WebParser.Parser
{
    public sealed class ContentParser : IContentParser, IDisposable
    {
        private const int CharsetDetectBytes = 4 * 1024;
        private const int HostSemsPruneThreshold = 5000;
        private const long HostIdleMs = 10 * 60 * 1000;
        private const long PruneCheckIntervalMs = 30 * 1000;
        private static readonly byte[] MetaCharsetPattern = Encoding.ASCII.GetBytes("charset=");

        private readonly ConcurrentDictionary<string, SemaphoreSlim> _hostSems = new();
        private readonly ConcurrentDictionary<string, long> _hostLastUse = new();
        private long _lastPruneCheck;

        private readonly ILogger _logger;
        private readonly HttpClient _http;
        private readonly CrawlerOptions _options;
        private readonly bool _verbose;

        static ContentParser()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public ContentParser(ILogger logger, CrawlerOptions options, bool verbose = false)
        {
            _logger = logger;
            _options = options;
            _verbose = verbose;
            _http = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All
            })
            {
                Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
                DefaultRequestHeaders =
                {
                    { "User-Agent", options.UserAgent }
                }
            };
        }

        public void Dispose()
        {
            _http.Dispose();
        }

        public async Task<ParseResult> ParseAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    !(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    _logger.Log(new LogMessage { Message = $"Некорректный адрес: {url}" });
                    return new ParseResult(ParseOutcome.InvalidUrl);
                }

                _logger.Log(new LogMessage { Message = $"Обработка адреса {url}" });

                using var hostGate = await AcquireHostGateAsync(uri.Host, cancellationToken);
                using var response = await GetWithRetryAsync(uri, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.Log(new LogMessage
                    {
                        Message = $"Не удалось обработать адрес {url}. Код ответа: {(int)response.StatusCode}"
                    });
                    return new ParseResult(ParseOutcome.HttpError);
                }

                var words = await CountWordsAsync(response, cancellationToken);

                _logger.Log(new LogMessage { Message = $"Обработка адреса {url} завершена. Результаты обработки:" });
                foreach (var item in words)
                {
                    string msg = $"Слово: {item.Word}\tКоличество повторов: {item.Frequency}";
                    if (_verbose)
                    {
                        Console.WriteLine(msg);
                    }
                    _logger.Log(new LogMessage { Message = msg, Level = LogLevel.Verbose });
                }

                return new ParseResult(ParseOutcome.Success, words);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.Log(new LogMessage { Message = $"Прервано по таймауту: {url}" });
                return new ParseResult(ParseOutcome.Failed);
            }
            catch (PageTooLargeException)
            {
                _logger.Log(new LogMessage { Message = $"Слишком большой ответ от адреса {url}" });
                return new ParseResult(ParseOutcome.PageTooLarge);
            }
            catch (Exception ex)
            {
                _logger.Log(new LogMessage { Message = $"Ошибка при обработке адреса {url}", Exception = ex });
                return new ParseResult(ParseOutcome.Failed);
            }
        }

        private async Task<HttpResponseMessage> GetWithRetryAsync(Uri uri, CancellationToken cancellationToken)
        {
            HttpResponseMessage? response = null;

            for (int attempt = 0; attempt < _options.MaxAttempts; attempt++)
            {
                response?.Dispose();

                try
                {
                    response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                }
                catch (HttpRequestException) when (CanRetry(attempt, cancellationToken))
                {
                    await RetryBackoffAsync(uri, attempt, null, cancellationToken);
                    continue;
                }
                catch (TaskCanceledException) when (CanRetry(attempt, cancellationToken))
                {
                    await RetryBackoffAsync(uri, attempt, null, cancellationToken);
                    continue;
                }

                if (response.IsSuccessStatusCode || !IsRetryable(response.StatusCode))
                {
                    return response;
                }

                if (!CanRetry(attempt, cancellationToken))
                {
                    return response;
                }

                await RetryBackoffAsync(uri, attempt, response, cancellationToken);
            }

            throw new InvalidOperationException($"Невозможно получить ответ от {uri}");
        }

        private bool CanRetry(int attempt, CancellationToken cancellationToken)
        {
            return attempt < _options.MaxAttempts - 1 && !cancellationToken.IsCancellationRequested;
        }

        private static bool IsRetryable(HttpStatusCode statusCode)
        {
            int code = (int)statusCode;
            return statusCode == HttpStatusCode.TooManyRequests ||
                   statusCode == HttpStatusCode.RequestTimeout ||
                   code == 500 ||
                   statusCode == HttpStatusCode.BadGateway ||
                   statusCode == HttpStatusCode.ServiceUnavailable ||
                   statusCode == HttpStatusCode.GatewayTimeout;
        }

        private async Task RetryBackoffAsync(Uri uri, int attempt, HttpResponseMessage? response, CancellationToken cancellationToken)
        {
            int maxAttempts = _options.MaxAttempts;
            TimeSpan delay = GetRetryDelay(attempt);

            if (response != null && response.StatusCode == HttpStatusCode.TooManyRequests &&
                response.Headers.RetryAfter?.Delta is TimeSpan retryAfter)
            {
                TimeSpan cap = TimeSpan.FromSeconds(_options.RetryAfterCapSeconds);
                delay = retryAfter > cap ? cap : retryAfter;
            }

            _logger.Log(new LogMessage
            {
                Message = $"Повтор запроса к {uri} через {delay.TotalSeconds:F1} с (попытка {attempt + 2} из {maxAttempts})"
            });

            await Task.Delay(delay, cancellationToken);
        }

        private TimeSpan GetRetryDelay(int attempt)
        {
            double baseDelayMs = _options.RetryBaseDelayMs * Math.Pow(2, attempt);
            double jitter = (Random.Shared.NextDouble() - 0.5) * baseDelayMs;
            return TimeSpan.FromMilliseconds(Math.Max(0, baseDelayMs + jitter));
        }

        private async Task<IDisposable> AcquireHostGateAsync(string host, CancellationToken cancellationToken)
        {
            PruneHostSemsIfNeeded();
            SemaphoreSlim semaphore = _hostSems.GetOrAdd(host, _ => new SemaphoreSlim(_options.MaxConcurrentPerHost));
            await semaphore.WaitAsync(cancellationToken);
            _hostLastUse[host] = Environment.TickCount64;
            return new HostGate(semaphore);
        }

        private void PruneHostSemsIfNeeded()
        {
            long now = Environment.TickCount64;
            if (now - _lastPruneCheck < PruneCheckIntervalMs)
            {
                return;
            }

            _lastPruneCheck = now;

            if (_hostSems.Count < HostSemsPruneThreshold)
            {
                return;
            }

            foreach (string host in _hostSems.Keys)
            {
                if (_hostLastUse.TryGetValue(host, out long last) && now - last > HostIdleMs)
                {
                    _hostLastUse.TryRemove(host, out _);
                    _hostSems.TryRemove(host, out _);
                }
            }
        }

        private async Task<IReadOnlyList<WordFrequency>> CountWordsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            string? headerCharset = response.Content.Headers.ContentType?.CharSet;
            Stream network = await response.Content.ReadAsStreamAsync(cancellationToken);

            byte[] prefix = ArrayPool<byte>.Shared.Rent(CharsetDetectBytes);
            byte[] chunk = ArrayPool<byte>.Shared.Rent(81920);
            char[] buffer = ArrayPool<char>.Shared.Rent(8192);

            try
            {
                int prefixLen = await ReadPrefixAsync(network, prefix, chunk, _options.MaxPageSizeBytes, cancellationToken);
                Encoding encoding = ResolveEncoding(prefix, prefixLen, headerCharset);

                using var bounded = new BoundedReadStream(network, prefix, prefixLen, prefixLen, _options.MaxPageSizeBytes);
                using var reader = new StreamReader(bounded, encoding, detectEncodingFromByteOrderMarks: true);
                var counter = new HtmlWordCounter();

                int left;
                while ((left = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    counter.Process(buffer.AsSpan(0, left));
                }

                return counter.Complete();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(prefix);
                ArrayPool<byte>.Shared.Return(chunk);
                ArrayPool<char>.Shared.Return(buffer);
            }
        }

        private static async Task<int> ReadPrefixAsync(Stream stream, byte[] prefix, byte[] chunk, long maxBytes, CancellationToken cancellationToken)
        {
            int prefixLen = 0;
            long total = 0;
            while (prefixLen < CharsetDetectBytes)
            {
                int toRead = Math.Min(chunk.Length, CharsetDetectBytes - prefixLen);
                int read = await stream.ReadAsync(chunk.AsMemory(0, toRead), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > maxBytes)
                {
                    throw new PageTooLargeException((int)maxBytes);
                }

                Array.Copy(chunk, 0, prefix, prefixLen, read);
                prefixLen += read;
            }

            return prefixLen;
        }

        private static Encoding ResolveEncoding(byte[] body, int length, string? headerCharset)
        {
            Encoding? encoding = GetEncodingSafe(headerCharset);
            if (encoding != null)
            {
                return encoding;
            }

            return GetEncodingFromMeta(body, length) ?? Encoding.UTF8;
        }

        private sealed class BoundedReadStream : Stream
        {
            private readonly Stream _inner;
            private readonly byte[] _prefix;
            private readonly int _prefixLen;
            private readonly long _maxBytes;
            private int _prefixPos;
            private long _total;

            public BoundedReadStream(Stream inner, byte[] prefix, int prefixLen, long total, long maxBytes)
            {
                _inner = inner;
                _prefix = prefix;
                _prefixLen = prefixLen;
                _total = total;
                _maxBytes = maxBytes;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (_prefixPos < _prefixLen)
                {
                    int n = Math.Min(buffer.Length, _prefixLen - _prefixPos);
                    _prefix.AsMemory(_prefixPos, n).CopyTo(buffer);
                    _prefixPos += n;
                    _total += n;
                    EnsureWithinLimit();
                    return n;
                }

                int read = await _inner.ReadAsync(buffer, cancellationToken);
                _total += read;
                EnsureWithinLimit();
                return read;
            }

            private void EnsureWithinLimit()
            {
                if (_total > _maxBytes)
                {
                    throw new PageTooLargeException((int)_maxBytes);
                }
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private static Encoding? GetEncodingSafe(string? charset)
        {
            if (string.IsNullOrWhiteSpace(charset))
            {
                return null;
            }

            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        private static Encoding? GetEncodingFromMeta(byte[] body, int length)
        {
            int window = Math.Min(length, CharsetDetectBytes);
            int marker = IndexOfCharsetPattern(body, window);
            if (marker < 0)
            {
                return null;
            }

            int start = marker + MetaCharsetPattern.Length;
            int end = Math.Min(window, start + 128);
            string name = Encoding.ASCII.GetString(body, start, end - start);
            int nameEnd = 0;
            while (nameEnd < name.Length && !IsDelimiter(name[nameEnd]))
            {
                nameEnd++;
            }

            return GetEncodingSafe(name.AsSpan(0, nameEnd).Trim().ToString());
        }

        private static int IndexOfCharsetPattern(byte[] data, int length)
        {
            ReadOnlySpan<byte> pattern = MetaCharsetPattern;
            ReadOnlySpan<byte> source = data.AsSpan(0, length);
            for (int i = 0; i + pattern.Length <= source.Length; i++)
            {
                if (LowerAscii(source[i]) != pattern[0])
                {
                    continue;
                }

                bool matched = true;
                for (int j = 1; j < pattern.Length; j++)
                {
                    if (LowerAscii(source[i + j]) != pattern[j])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return i;
                }
            }

            return -1;
        }

        private static byte LowerAscii(byte b)
        {
            return b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b | 0x20) : b;
        }

        private static bool IsDelimiter(char c)
        {
            return c == '"' || c == '\'' || c == '>' || c == ' ' || c == ';' || c == '\n' || c == '\r' || c == '\t' || c == '/';
        }

        private readonly struct HostGate : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;

            public HostGate(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
            }

            public void Dispose()
            {
                _semaphore.Release();
            }
        }
    }
}