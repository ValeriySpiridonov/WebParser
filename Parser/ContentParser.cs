using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebParser.Config;
using WebParser.Logger;

namespace WebParser.Parser
{
    public sealed class ContentParser : IContentParser
    {
        private const int CharsetDetectBytes = 4 * 1024;
        private const int HostSemsPruneThreshold = 5000;
        private const long HostIdleMs = 10 * 60 * 1000;

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> HostSems = new();
        private static readonly ConcurrentDictionary<string, long> HostLastUse = new();

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

        public async Task<ParseResult> ParseAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    !(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    _logger.Log(new LogMessage { Message = $"Некорректный адрес: {url}" });
                    return new ParseResult(false);
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
                    return new ParseResult(false);
                }

                var result = await CountWordsAsync(response, cancellationToken);

                _logger.Log(new LogMessage { Message = $"Обработка адреса {url} завершена. Результаты обработки:" });
                foreach (var item in result)
                {
                    string msg = $"Слово: {item.Word}\tКоличество повторов: {item.Frequency}";
                    if (_verbose)
                    {
                        Console.WriteLine(msg);
                    }
                    _logger.Log(new LogMessage { Message = msg, Level = LogLevel.Verbose });
                }

                return new ParseResult(true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PageTooLargeException)
            {
                _logger.Log(new LogMessage { Message = $"Слишком большой ответ от адреса {url}" });
                return new ParseResult(false);
            }
            catch (Exception ex)
            {
                _logger.Log(new LogMessage { Message = $"Ошибка при обработке адреса {url}", Exception = ex });
                return new ParseResult(false);
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
            SemaphoreSlim semaphore = HostSems.GetOrAdd(host, _ => new SemaphoreSlim(_options.MaxConcurrentPerHost));
            HostLastUse[host] = Environment.TickCount64;
            await semaphore.WaitAsync(cancellationToken);
            return new HostGate(semaphore);
        }

        private static void PruneHostSemsIfNeeded()
        {
            if (HostSems.Count < HostSemsPruneThreshold)
            {
                return;
            }

            long now = Environment.TickCount64;
            foreach (string host in HostSems.Keys)
            {
                if (HostLastUse.TryGetValue(host, out long last) && now - last > HostIdleMs)
                {
                    HostLastUse.TryRemove(host, out _);
                    HostSems.TryRemove(host, out _);
                }
            }
        }

        private async Task<IReadOnlyList<WordFrequency>> CountWordsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            string? headerCharset = response.Content.Headers.ContentType?.CharSet;

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            byte[] body = await ReadBoundedAsync(stream, _options.MaxPageSizeBytes, cancellationToken);
            Encoding encoding = ResolveEncoding(body, headerCharset);

            using var reader = new StreamReader(new MemoryStream(body), encoding, detectEncodingFromByteOrderMarks: true);
            var counter = new HtmlWordCounter();
            char[] buffer = ArrayPool<char>.Shared.Rent(8192);

            try
            {
                int read;
                while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    counter.Process(buffer.AsSpan(0, read));
                }

                return counter.Complete();
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }

        private static async Task<byte[]> ReadBoundedAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream((int)Math.Min(maxBytes, int.MaxValue));
            var chunk = new byte[81920];

            while (true)
            {
                int read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (buffer.Length + read > maxBytes)
                {
                    throw new PageTooLargeException((int)maxBytes);
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }

        private static Encoding ResolveEncoding(byte[] body, string? headerCharset)
        {
            Encoding? encoding = GetEncodingSafe(headerCharset);
            if (encoding != null)
            {
                return encoding;
            }

            return GetEncodingFromMeta(body) ?? Encoding.UTF8;
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

        private static Encoding? GetEncodingFromMeta(byte[] body)
        {
            int length = Math.Min(body.Length, CharsetDetectBytes);
            string head = Encoding.ASCII.GetString(body, 0, length);

            int marker = head.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
            {
                return null;
            }

            int start = marker + "charset=".Length;
            int end = start;
            while (end < head.Length && !IsDelimiter(head[end]))
            {
                end++;
            }

            return GetEncodingSafe(head.Substring(start, end - start).Trim());
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