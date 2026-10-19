namespace WebParser
{
    /// <summary>
    /// Аргументы командной строки: режим verbose и путь к файлу со списком адресов.
    /// </summary>
    public sealed record CliArgs(bool Verbose, string? UrlFile)
    {
        /// <summary>
        /// Разбирает аргументы командной строки: флаги могут стоять в любом порядке,
        /// первый аргумент без дефиса — путь к файлу со списком адресов.
        /// </summary>
        /// <param name="args">Аргументы командной строки.</param>
        /// <returns>Разобранные аргументы; null, если встретилась неизвестная опция.</returns>
        public static CliArgs? Parse(string[] args)
        {
            bool verbose = false;
            string? urlFile = null;

            foreach (string arg in args)
            {
                if (arg is "--verbose" or "-v")
                {
                    verbose = true;
                }
                else if (arg.StartsWith('-'))
                {
                    Console.Error.WriteLine($"Неизвестная опция: {arg}");
                    Environment.ExitCode = 1;
                    return null;
                }
                else if (urlFile == null)
                {
                    urlFile = arg;
                }
            }

            return new CliArgs(verbose, urlFile);
        }
    }
}
