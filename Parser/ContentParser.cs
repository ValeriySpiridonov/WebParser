using System.Buffers;
using System.Text;
using WebParser.Config;
using WebParser.Logger;

namespace WebParser.Parser
{
    /// <summary>
    /// Скачивает страницу по адресу, парсит текст и считает частоту слов.
    /// </summary>
    public sealed class ContentParser : IContentParser, IDisposable
    {
        private readonly ILogger _logger;
        private readonly IHttpFetcher _fetcher;
        private readonly IHostRateLimiter _hostLimiter;
        private readonly CrawlerOptions _options;
        private readonly bool _ownsFetcher;

        /// <summary>
        /// Создаёт обработчик страниц с собственным HTTP-клиентом и лимитером.
        /// </summary>
        /// <param name="logger">Журнал для событий обработки.</param>
        /// <param name="options">Параметры краулера.</param>
        public ContentParser(ILogger logger, CrawlerOptions options)
            : this(logger, options, new HttpFetcher(options, logger), new HostRateLimiter(options.MaxConcurrentPerHost))
        {
            _ownsFetcher = true;
        }

        /// <summary>
        /// Создаёт обработчик страниц с инжектированными фетчером и лимитером; переданные зависимости освобождаются вызывающим.
        /// </summary>
        /// <param name="logger">Журнал для событий обработки.</param>
        /// <param name="options">Параметры краулера.</param>
        /// <param name="fetcher">Фетчер для скачивания страниц.</param>
        /// <param name="hostLimiter">Лимитер одновременных операций на хост.</param>
        public ContentParser(ILogger logger, CrawlerOptions options, IHttpFetcher fetcher, IHostRateLimiter hostLimiter)
        {
            _logger = logger;
            _options = options;
            _fetcher = fetcher;
            _hostLimiter = hostLimiter;
        }

        /// <summary>
        /// Освобождает HTTP-клиент, если фетчер был создан этим обработчиком.
        /// </summary>
        public void Dispose()
        {
            if (_ownsFetcher)
            {
                _fetcher.Dispose();
            }
        }

        /// <summary>
        /// Скачивает страницу по адресу, считает частоты слов и возвращает результат.
        /// </summary>
        /// <param name="url">Полный HTTP(S) адрес страницы.</param>
        /// <param name="cancellationToken">Токен отмены операции.</param>
        /// <returns>Результат обработки; реальная отмена выбрасывает OperationCanceledException.</returns>
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

                using var hostGate = await _hostLimiter.AcquireAsync(uri.Host, cancellationToken);
                using var response = await _fetcher.GetAsync(uri, cancellationToken);

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
                    _logger.Log(new LogMessage
                    {
                        Message = $"Слово: {item.Word}\tКоличество повторов: {item.Frequency}",
                        Level = LogLevel.Verbose
                    });
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

        private async Task<IReadOnlyList<WordFrequency>> CountWordsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            string? headerCharset = response.Content.Headers.ContentType?.CharSet;
            Stream network = await response.Content.ReadAsStreamAsync(cancellationToken);

            byte[] prefix = ArrayPool<byte>.Shared.Rent(CharsetDetector.DetectPrefixBytes);
            byte[] chunk = ArrayPool<byte>.Shared.Rent(81920);
            char[] buffer = ArrayPool<char>.Shared.Rent(8192);

            try
            {
                int prefixLen = await ReadPrefixAsync(network, prefix, chunk, _options.MaxPageSizeBytes, cancellationToken);
                Encoding encoding = CharsetDetector.Resolve(prefix.AsSpan(0, prefixLen), headerCharset);

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
            while (prefixLen < CharsetDetector.DetectPrefixBytes)
            {
                int toRead = Math.Min(chunk.Length, CharsetDetector.DetectPrefixBytes - prefixLen);
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
    }
}