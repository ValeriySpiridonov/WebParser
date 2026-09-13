using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebParser.Logger;
using WebParser.Parser;

namespace WebParser
{
    class Program
    {
        private const int MaxConcurrency = 10;

        static async Task Main(string[] args)
        {
            bool verbose = args.Length > 0 && (args[0] == "--verbose" || args[0] == "-v");
            string? urlFile = verbose ? (args.Length > 1 ? args[1] : null) : (args.Length > 0 ? args[0] : null);

            List<string> urls = urlFile != null ? ReadUrlsFromFile(urlFile) : ReadUrlsFromConsole();
            if (urls.Count == 0)
            {
                Console.Error.WriteLine("Не указано ни одного корректного адреса");
                return;
            }

            using var logger = new AsyncFileLogger(verbose ? LogLevel.Verbose : LogLevel.Info);
            var parser = new ContentParser(logger);
            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            logger.Log(new LogMessage { Message = $"Начата обработка {urls.Count} адресов" });

            try
            {
                await Parallel.ForEachAsync(urls,
                    new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency, CancellationToken = cts.Token },
                    async (url, ct) => await parser.ParseAsync(url, ct));

                logger.Log(new LogMessage { Message = "Обработка завершена" });
            }
            catch (OperationCanceledException)
            {
                logger.Log(new LogMessage { Message = "Обработка прервана пользователем" });
            }

            try
            {
                Console.ReadKey();
            }
            catch (InvalidOperationException)
            {
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