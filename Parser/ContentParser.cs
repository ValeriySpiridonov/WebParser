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
using WebParser.Logger;

namespace WebParser.Parser
{
    public class ContentParser : IContentParser
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> HostSems =
            new ConcurrentDictionary<string, SemaphoreSlim>();

        private const int MaxAttempts = 3;
        private const int MaxConcurrentPerHost = 2;
        private const int MaxPageSizeBytes = 5 * 1024 * 1024;
        private const int CharsetDetectBytes = 4 * 1024;
        private static readonly TimeSpan MaxRetryAfterCap = TimeSpan.FromSeconds(30);

        private readonly ILogger _logger;

        static ContentParser()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public ContentParser(ILogger logger)
        {
            _logger = logger;
        }

        public async Task ParseAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    !(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    _logger.Log(new LogMessage { Message = $"Некорректный адрес: {url}" });
                    return;
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
                    return;
                }

                var result = await CountWordsAsync(response, cancellationToken);

                _logger.Log(new LogMessage { Message = $"Обработка адреса {url} завершена. Результаты обработки:" });
                foreach (var item in result)
                {
                    string msg = $"Слово: {item.Word}\tКоличество повторов: {item.Frequency}";
                    Console.WriteLine(msg);
                    _logger.Log(new LogMessage { Message = msg });
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Log(new LogMessage { Message = $"Обработка адреса {url} прервана" });
            }
            catch (PageTooLargeException)
            {
                _logger.Log(new LogMessage { Message = $"Слишком большой ответ от адреса {url}" });
            }
            catch (Exception ex)
            {
                _logger.Log(new LogMessage { Message = $"Ошибка при обработке адреса {url}", Exception = ex });
            }
        }

        private async Task<HttpResponseMessage> GetWithRetryAsync(Uri uri, CancellationToken cancellationToken)
        {
            HttpResponseMessage? response = null;

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                response?.Dispose();

                try
                {
                    response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

        private static bool CanRetry(int attempt, CancellationToken cancellationToken)
        {
            return attempt < MaxAttempts - 1 && !cancellationToken.IsCancellationRequested;
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
            TimeSpan delay = GetRetryDelay(attempt);

            if (response != null && response.StatusCode == HttpStatusCode.TooManyRequests &&
                response.Headers.RetryAfter?.Delta is TimeSpan retryAfter)
            {
                delay = retryAfter > MaxRetryAfterCap ? MaxRetryAfterCap : retryAfter;
            }

            _logger.Log(new LogMessage
            {
                Message = $"Повтор запроса к {uri} через {delay.TotalSeconds:F1} с (попытка {attempt + 2} из {MaxAttempts})"
            });

            await Task.Delay(delay, cancellationToken);
        }

        private static TimeSpan GetRetryDelay(int attempt)
        {
            double baseDelayMs = 500 * Math.Pow(2, attempt);
            double jitter = (Random.Shared.NextDouble() - 0.5) * baseDelayMs;
            return TimeSpan.FromMilliseconds(Math.Max(0, baseDelayMs + jitter));
        }

        private static async Task<IDisposable> AcquireHostGateAsync(string host, CancellationToken cancellationToken)
        {
            SemaphoreSlim semaphore = HostSems.GetOrAdd(host, static _ => new SemaphoreSlim(MaxConcurrentPerHost));
            await semaphore.WaitAsync(cancellationToken);
            return new HostGate(semaphore);
        }

        private async Task<IReadOnlyList<WordFrequency>> CountWordsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            string? headerCharset = response.Content.Headers.ContentType?.CharSet;

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            byte[] body = await ReadBoundedAsync(stream, cancellationToken);
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

        private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream(MaxPageSizeBytes);
            var chunk = new byte[81920];

            while (true)
            {
                int read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (buffer.Length + read > MaxPageSizeBytes)
                {
                    throw new PageTooLargeException(MaxPageSizeBytes);
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