using WebParser.Config;
using WebParser.Logger;
using WebParser.Parser;

namespace WebParser.Tests;

/// <summary>
/// Обёртка над <see cref="ContentParser"/> для тестов: владеет фетчером
/// и освобождает его вместе с собой.
/// </summary>
internal sealed class ParserScope : IDisposable
{
    private readonly HttpFetcher _fetcher;

    /// <summary>
    /// Создаёт обработчик страниц с собственным фетчером и лимитером хостов.
    /// </summary>
    /// <param name="logger">Журнал для событий обработки.</param>
    /// <param name="options">Параметры краулера.</param>
    public ParserScope(ILogger logger, CrawlerOptions options)
    {
        _fetcher = new HttpFetcher(options, logger);
        Parser = new ContentParser(logger, options, _fetcher, new HostRateLimiter(options));
    }

    /// <summary>Обработчик страниц под тестом.</summary>
    public ContentParser Parser { get; }

    /// <summary>Скачивает страницу и считает частоты слов.</summary>
    public Task<ParseResult> ParseAsync(string url, CancellationToken cancellationToken = default)
        => Parser.ParseAsync(url, cancellationToken);

    /// <summary>Освобождает фетчер.</summary>
    public void Dispose() => _fetcher.Dispose();
}
