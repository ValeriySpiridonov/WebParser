using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            List<string> urls = args.Length > 0 ? ReadUrlsFromFile(args[0]) : ReadUrlsFromConsole();
            if (urls.Count == 0)
            {
                Console.Error.WriteLine("Не указано ни одного корректного адреса");
                return;
            }

            using var logger = new DisposableFileLogger();
            var parser = new ContentParser(logger);

            logger.Log(new LogMessage { Message = $"Начата обработка {urls.Count} адресов" });

            await Parallel.ForEachAsync(urls,
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency },
                async (url, _) => await parser.ParseAsync(url));

            logger.Log(new LogMessage { Message = "Обработка завершена" });

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
            string line;
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
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }
    }
}