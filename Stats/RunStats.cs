using System.Diagnostics;

namespace WebParser.Stats
{
    /// <summary>
    /// Собирает потокобезопасные метрики прогона: обработано, ошибки, RPS и среднее время.
    /// </summary>
    public sealed class RunStats
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _processed;
        private long _errors;
        private long _successDurationMs;

        /// <summary>
        /// Учитывает результат обработки одного адреса.
        /// </summary>
        /// <param name="succeeded">True, если адрес обработан успешно.</param>
        /// <param name="duration">Затраченное на обработку время.</param>
        public void Record(bool succeeded, TimeSpan duration)
        {
            long ticks = (long)duration.TotalMilliseconds;
            if (succeeded)
            {
                Interlocked.Increment(ref _processed);
                Interlocked.Add(ref _successDurationMs, ticks);
            }
            else
            {
                Interlocked.Increment(ref _errors);
            }
        }

        /// <summary>
        /// Возвращает текущий снимок метрик прогона.
        /// </summary>
        /// <returns>Кортеж: обработано, ошибки, RPS, среднее время в мс, прошедшее время.</returns>
        public (long Processed, long Errors, double Rps, double AverageMs, TimeSpan Elapsed) Snapshot()
        {
            long processed = Interlocked.Read(ref _processed);
            long errors = Interlocked.Read(ref _errors);
            long successDurationMs = Interlocked.Read(ref _successDurationMs);
            TimeSpan elapsed = _clock.Elapsed;
            double rps = elapsed.TotalSeconds > 0 ? processed / elapsed.TotalSeconds : 0;
            double averageMs = processed > 0 ? successDurationMs / (double)processed : 0;
            return (processed, errors, rps, averageMs, elapsed);
        }

        /// <summary>
        /// Формирует итоговую строку метрик для вывода пользователю.
        /// </summary>
        /// <returns>Строка вида «Обработано: N, Ошибок: M, RPS: .., Среднее: .. мс, Прошло: ..».</returns>
        public string ToLine()
        {
            var (processed, errors, rps, averageMs, elapsed) = Snapshot();
            return $"Обработано: {processed}, Ошибок: {errors}, RPS: {rps:F1}, Среднее: {averageMs:F0} мс, " +
                   $"Прошло: {elapsed:hh\\:mm\\:ss}";
        }
    }
}