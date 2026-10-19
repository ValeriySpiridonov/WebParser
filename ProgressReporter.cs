using WebParser.Stats;

namespace WebParser
{
    /// <summary>
    /// Периодически выводит метрики прогона в одну строку консоли (\r),
    /// пока не будет освобождён.
    /// </summary>
    public sealed class ProgressReporter : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        /// <summary>
        /// Запускает цикл вывода метрик.
        /// </summary>
        /// <param name="stats">Сборщик метрик прогона.</param>
        public ProgressReporter(RunStats stats)
        {
            _loop = RunLoopAsync(stats, _cts.Token);
        }

        /// <summary>
        /// Останавливает цикл вывода и дожидается его завершения.
        /// </summary>
        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                _loop.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }

            _cts.Dispose();
        }

        private static async Task RunLoopAsync(RunStats stats, CancellationToken token)
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
    }
}
