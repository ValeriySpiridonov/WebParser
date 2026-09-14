using System.Diagnostics;

namespace WebParser.Stats
{
    public sealed class RunStats
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _processed;
        private long _errors;
        private long _successDurationMs;

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

        public string ToLine()
        {
            var (processed, errors, rps, averageMs, elapsed) = Snapshot();
            return $"Обработано: {processed}, Ошибок: {errors}, RPS: {rps:F1}, Среднее: {averageMs:F0} мс, " +
                   $"Прошло: {elapsed:hh\\:mm\\:ss}";
        }
    }
}