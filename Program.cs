using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using WebParser.Config;
using WebParser.Logger;
using WebParser.Parser;
using WebParser.Stats;

namespace WebParser
{
    class Program
    {
        private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);

        static async Task Main(string[] args)
        {
            bool verbose = args.Length > 0 && (args[0] == "--verbose" || args[0] == "-v");
            string? urlFile = verbose ? (args.Length > 1 ? args[1] : null) : (args.Length > 0 ? args[0] : null);

            var options = ConfigLoader.Load();
            List<string> urls = urlFile != null ? ReadUrlsFromFile(urlFile) : ReadUrlsFromConsole();
            if (urls.Count == 0)
            {
                Console.Error.WriteLine("Не указано ни одного корректного адреса");
                return;
            }

            var loggerMinLevel = verbose ? LogLevel.Verbose : options.Logging.MinLevel;

            var services = new ServiceCollection();
            services.AddSingleton(options.Crawler);
            services.AddSingleton(options.Logging);
            services.AddSingleton<ILogger>(sp => new AsyncFileLogger(
                sp.GetRequiredService<LoggingOptions>(),
                loggerMinLevel,
                Console.Error));
            services.AddSingleton<IHttpFetcher>(sp => new HttpFetcher(
                sp.GetRequiredService<CrawlerOptions>(),
                sp.GetRequiredService<ILogger>()));
            services.AddSingleton<IHostRateLimiter>(sp => new HostRateLimiter(
                sp.GetRequiredService<CrawlerOptions>().MaxConcurrentPerHost));
            services.AddSingleton<IContentParser>(sp => new ContentParser(
                sp.GetRequiredService<ILogger>(),
                sp.GetRequiredService<CrawlerOptions>(),
                sp.GetRequiredService<IHttpFetcher>(),
                sp.GetRequiredService<IHostRateLimiter>()));

            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            var logger = provider.GetRequiredService<ILogger>();
            var parser = provider.GetRequiredService<IContentParser>();
            var stats = new RunStats();

            using var schedCts = new CancellationTokenSource();
            using var hardCts = new CancellationTokenSource();
            Task? hardCancelTask = null;

            Console.CancelKeyPress += (_, e) =>
            {
                if (schedCts.IsCancellationRequested)
                {
                    return;
                }

                e.Cancel = true;
                schedCts.Cancel();
                hardCancelTask = Task.Run(async () =>
                {
                    await Task.Delay(GracePeriod);
                    hardCts.Cancel();
                });
            };

            logger.Log(new LogMessage { Message = $"Начата обработка {urls.Count} адресов" });

            using var progressCts = CancellationTokenSource.CreateLinkedTokenSource(schedCts.Token);
            Task? progressTask = verbose ? null : RunProgressLoopAsync(stats, progressCts.Token);

            bool cancelledByUser = false;
            try
            {
                await Parallel.ForEachAsync(urls,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = options.Crawler.MaxConcurrency,
                        CancellationToken = schedCts.Token
                    },
                    async (url, ct) =>
                    {
                        var sw = Stopwatch.StartNew();
                        var result = await parser.ParseAsync(url, hardCts.Token);
                        sw.Stop();
                        stats.Record(result.Succeeded, sw.Elapsed);
                        if (verbose && result.Outcome == ParseOutcome.Success && result.Words is { } words)
                        {
                            foreach (var item in words)
                            {
                                Console.WriteLine($"Слово: {item.Word}\tКоличество повторов: {item.Frequency}");
                            }
                        }
                    });

                logger.Log(new LogMessage { Message = "Обработка завершена" });
            }
            catch (OperationCanceledException)
            {
                cancelledByUser = true;
                logger.Log(new LogMessage { Message = "Обработка прервана пользователем" });
            }
            catch (AggregateException) when (schedCts.IsCancellationRequested || hardCts.IsCancellationRequested)
            {
                cancelledByUser = true;
                logger.Log(new LogMessage { Message = "Обработка прервана пользователем" });
            }

            progressCts.Cancel();
            if (progressTask != null)
            {
                try
                {
                    await progressTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            if (hardCancelTask != null)
            {
                await hardCancelTask;
            }

            Console.WriteLine($"\r{stats.ToLine()}");
            logger.Log(new LogMessage { Message = $"Итог: {stats.ToLine()}" });

            if (cancelledByUser)
            {
                Console.WriteLine("Обработка прервана пользователем");
            }

            try
            {
                Console.WriteLine("Нажмите любую клавишу для выхода...");
                Console.ReadKey();
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static async Task RunProgressLoopAsync(RunStats stats, CancellationToken token)
        {
            int prevLength = 0;
            while (true)
            {
                string line = stats.ToLine();
                int pad = Math.Max(0, prevLength - line.Length) + 4;
                Console.Write($"\r{line}{new string(' ', pad)}");
                prevLength = line.Length;

                try
                {
                    await Task.Delay(1000, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private static List<string> ReadUrlsFromFile(string path)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Файл не найден: {path}");
                Environment.ExitCode = 1;
                return new List<string>();
            }

            return File.ReadLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Where(IsValidUrl)
                .ToList();
        }

        private static List<string> ReadUrlsFromConsole()
        {
            Console.WriteLine("Введите адреса сайтов, по одному в строке. Для завершения ввода — пустая строка.");
            var urls = new List<string>();
            string? line;
            while (!string.IsNullOrWhiteSpace(line = Console.ReadLine()))
            {
                if (IsValidUrl(line))
                {
                    urls.Add(line);
                }
                else
                {
                    Console.Error.WriteLine($"Некорректный адрес: {line}");
                }
            }

            return urls;
        }

        private static bool IsValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }
    }
}