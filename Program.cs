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
            CliArgs? cli = CliArgs.Parse(args);
            if (cli == null)
            {
                return;
            }

            AppOptions options = ConfigLoader.Load(errors: Console.Error);
            List<string> urls = cli.UrlFile != null ? UrlSource.ReadFile(cli.UrlFile) : UrlSource.ReadConsole();
            if (urls.Count == 0)
            {
                Console.Error.WriteLine("Не указано ни одного корректного адреса");
                return;
            }

            LogLevel loggerMinLevel = cli.Verbose ? LogLevel.Verbose : options.Logging.MinLevel;

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
                sp.GetRequiredService<CrawlerOptions>()));
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

            var progress = cli.Verbose ? null : new ProgressReporter(stats);
            bool cancelled = await CrawlRunner.RunAsync(
                urls, parser, stats, logger, options.Crawler, cli.Verbose, schedCts.Token, hardCts.Token);
            progress?.Dispose();

            if (hardCancelTask != null)
            {
                await hardCancelTask;
            }

            Console.WriteLine($"\r{stats.ToLine()}");
            logger.Log(new LogMessage { Message = $"Итог: {stats.ToLine()}" });

            if (cancelled)
            {
                Console.WriteLine(CrawlRunner.CancelledMessage);
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
    }
}
