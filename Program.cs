using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
            using var logger = new AsyncFileLogger(options.Logging, loggerMinLevel);
            var parser = new ContentParser(logger, options.Crawler, verbose);
            var stats = new RunStats();

            using var schedCts = new CancellationTokenSource();
            using var hardCts = new CancellationTokenSource();

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                if (schedCts.IsCancellationRequested)
                {
                    return;
                }

                schedCts.Cancel();
                _ = Task.Run(() =>
                {
                    Task.Delay(GracePeriod).Wait();
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
                        bool succeeded = (await parser.ParseAsync(url, hardCts.Token)).Succeeded;
                        sw.Stop();
                        stats.Record(succeeded, sw.Elapsed);
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

            Console.WriteLine($"\r{stats.ToLine()}");
            logger.Log(new LogMessage { Message = $"Итог: {stats.ToLine()}" });

            if (cancelledByUser)
            {
                Console.WriteLine("Обработка прервана пользователем");
            }

            try
            {
                Console.ReadKey();
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static async Task RunProgressLoopAsync(RunStats stats, CancellationToken token)
        {
            while (true)
            {
                Console.Write($"\r{stats.ToLine()}    ");
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