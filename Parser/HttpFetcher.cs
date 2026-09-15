using System.Net;
using WebParser.Config;
using WebParser.Logger;

namespace WebParser.Parser
{
    /// <summary>
    /// Скачивает страницу по URI с автоматическими повторами и exponential backoff.
    /// </summary>
    internal sealed class HttpFetcher : IHttpFetcher
    {
        private readonly HttpClient _http;
        private readonly CrawlerOptions _options;
        private readonly ILogger _logger;

        public HttpFetcher(CrawlerOptions options, ILogger logger)
        {
            _options = options;
            _logger = logger;
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

        /// <inheritdoc />
        public async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken cancellationToken = default)
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

            return response ?? throw new InvalidOperationException($"Невозможно получить ответ от {uri}");
        }

        public void Dispose()
        {
            _http.Dispose();
        }

        private bool CanRetry(int attempt, CancellationToken cancellationToken)
        {
            return attempt < _options.MaxAttempts - 1 && !cancellationToken.IsCancellationRequested;
        }

        private static bool IsRetryable(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.TooManyRequests ||
                   statusCode == HttpStatusCode.RequestTimeout ||
                   statusCode == HttpStatusCode.InternalServerError ||
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
    }
}