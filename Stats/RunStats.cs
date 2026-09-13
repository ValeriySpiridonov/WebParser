using System;
using System.Diagnostics;
using System.Threading;

namespace WebParser.Stats
{
    public sealed class RunStats
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _processed;
        private long _errors;
        private long _totalDurationMs;

        public long Processed => Interlocked.Read(ref _processed);
        public long Errors => Interlocked.Read(ref _errors);
        public double Rps => Elapsed.TotalSeconds > 0 ? Processed / Elapsed.TotalSeconds : 0;
        public double AverageMs => Processed > 0 ? _totalDurationMs / (double)Processed : 0;
        public TimeSpan Elapsed => _clock.Elapsed;

        public void Record(bool succeeded, TimeSpan duration)
        {
            Interlocked.Add(ref _totalDurationMs, (long)duration.TotalMilliseconds);
            if (succeeded)
            {
                Interlocked.Increment(ref _processed);
            }
            else
            {
                Interlocked.Increment(ref _errors);
            }
        }

        public string ToLine()
        {
            return $"Обработано: {Processed}, Ошибок: {Errors}, RPS: {Rps:F1}, Среднее: {AverageMs:F0} мс, " +
                   $"Прошло: {Elapsed:hh\\:mm\\:ss}";
        }
    }
}