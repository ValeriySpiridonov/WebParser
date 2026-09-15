using System.Collections.Concurrent;

namespace WebParser.Parser
{
    /// <summary>
    /// Ограничивает число одновременных операций на хост: по семафору на хост,
    /// простаивающие хосты периодически вычищаются.
    /// </summary>
    internal sealed class HostRateLimiter : IHostRateLimiter
    {
        private const int HostSemsPruneThreshold = 5000;
        private const long HostIdleMs = 10 * 60 * 1000;
        private const long PruneCheckIntervalMs = 30 * 1000;

        private readonly int _maxConcurrentPerHost;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _hostSems = new();
        private readonly ConcurrentDictionary<string, long> _hostLastUse = new();
        private long _lastPruneCheck;

        /// <summary>
        /// Создаёт лимитер с указанным числом слота на хост.
        /// </summary>
        /// <param name="maxConcurrentPerHost">Максимум одновременных операций на один хост.</param>
        public HostRateLimiter(int maxConcurrentPerHost)
        {
            _maxConcurrentPerHost = maxConcurrentPerHost;
        }

        /// <inheritdoc />
        public async Task<IDisposable> AcquireAsync(string host, CancellationToken cancellationToken = default)
        {
            PruneHostSemsIfNeeded();
            SemaphoreSlim semaphore = _hostSems.GetOrAdd(host, _ => new SemaphoreSlim(_maxConcurrentPerHost));
            await semaphore.WaitAsync(cancellationToken);
            _hostLastUse[host] = Environment.TickCount64;
            return new HostGate(semaphore);
        }

        private void PruneHostSemsIfNeeded()
        {
            long now = Environment.TickCount64;
            if (now - _lastPruneCheck < PruneCheckIntervalMs)
            {
                return;
            }

            _lastPruneCheck = now;

            if (_hostSems.Count < HostSemsPruneThreshold)
            {
                return;
            }

            foreach (string host in _hostSems.Keys)
            {
                if (_hostLastUse.TryGetValue(host, out long last) && now - last > HostIdleMs)
                {
                    _hostLastUse.TryRemove(host, out _);
                    _hostSems.TryRemove(host, out _);
                }
            }
        }

        private readonly struct HostGate : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;

            public HostGate(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
            }

            public void Dispose()
            {
                _semaphore.Release();
            }
        }
    }
}