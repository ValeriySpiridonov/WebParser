using WebParser.Parser;

namespace WebParser
{
    /// <summary>
    /// Читает список адресов для обработки из файла или консоли, отсеивая некорректные строки.
    /// </summary>
    public static class UrlSource
    {
        /// <summary>
        /// Читает адреса из файла; при отсутствии файла выводит ошибку и возвращает пустой список.
        /// </summary>
        /// <param name="path">Путь к файлу со списком адресов (по одному в строке).</param>
        /// <returns>Список корректных HTTP(S) адресов.</returns>
        public static List<string> ReadFile(string path)
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
                .Where(line => UrlValidator.TryParse(line, out _))
                .ToList();
        }

        /// <summary>
        /// Читает адреса из консоли до пустой строки, сообщая о некорректных строках.
        /// </summary>
        /// <returns>Список корректных HTTP(S) адресов.</returns>
        public static List<string> ReadConsole()
        {
            Console.WriteLine("Введите адреса сайтов, по одному в строке. Для завершения ввода — пустая строка.");
            var urls = new List<string>();
            string? line;
            while (!string.IsNullOrWhiteSpace(line = Console.ReadLine()))
            {
                if (UrlValidator.TryParse(line, out _))
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
    }
}
