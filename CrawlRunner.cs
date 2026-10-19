using System.Diagnostics;
using WebParser.Config;
using WebParser.Logger;
using WebParser.Parser;
using WebParser.Stats;

namespace WebParser
{
    /// <summary>
    /// Последовательно обрабатывает список адресов параллельно, собирая метрики и выводя
    /// результаты в режиме verbose; возвращает факт прерывания пользователем.
    /// </summary>
    public static class CrawlRunner
    {
        /// <summary>Сообщение о прерывании обработки пользователем.</summary>
        public const string CancelledMessage = "Обработка прервана пользователем";

        /// <summary>
        /// Обрабатывает все адреса и возвращает true, если обработка прервана пользователем.
        /// </summary>
        /// <param name="urls">Адреса для обработки.</param>
        /// <param name="parser">Обработчик страниц.</param>
        /// <param name="stats">Сборщик метрик прогона.</param>
        /// <param name="logger">Журнал событий.</param>
        /// <param name="options">Параметры краулера (параллельность).</param>
        /// <param name="verbose">True — выводить частоты слов в консоль и в журнал.</param>
        /// <param name="schedToken">Токен остановки планирования новых адресов.</param>
        /// <param name="hardToken">Токен жёсткой отмены текущих запросов.</param>
        /// <returns>True, если обработка прервана пользователем.</returns>
        public static async Task<bool> RunAsync(
            IReadOnlyList<string> urls,
            IContentParser parser,
            RunStats stats,
            ILogger logger,
            CrawlerOptions options,
            bool verbose,
            CancellationToken schedToken,
            CancellationToken hardToken)
        {
            try
            {
                await Parallel.ForEachAsync(urls,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = options.MaxConcurrency,
                        CancellationToken = schedToken
                    },
                    async (url, ct) =>
                    {
                        var sw = Stopwatch.StartNew();
                        ParseResult result = await parser.ParseAsync(url, hardToken);
                        sw.Stop();
                        stats.Record(result.Succeeded, sw.Elapsed);

                        if (verbose && result.Outcome == ParseOutcome.Success && result.Words is { } words)
                        {
                            foreach (WordFrequency item in words)
                            {
                                string line = WordFrequency.FormatLine(item);
                                Console.WriteLine(line);
                                logger.Log(new LogMessage { Message = line, Level = LogLevel.Verbose });
                            }
                        }
                    });

                logger.Log(new LogMessage { Message = "Обработка завершена" });
                return false;
            }
            catch (Exception ex) when (ex is OperationCanceledException ||
                                       schedToken.IsCancellationRequested ||
                                       hardToken.IsCancellationRequested)
            {
                logger.Log(new LogMessage { Message = CancelledMessage });
                return true;
            }
        }
    }
}
