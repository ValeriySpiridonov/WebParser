using System.Collections.Concurrent;
using WebParser.Config;

namespace WebParser.Parser
{
    /// <summary>
    /// Ограничивает число одновременных операций на хост: по семафору на хост,
    /// простаивающие хосты периодически вычищаются.
    /// </summary>
    internal sealed class HostRateLimiter : IHostRateLimiter
    {
        private readonly CrawlerOptions _options;
        private readonly ConcurrentDictionary<string, HostEntry> _hosts = new();
        private long _lastPruneCheck;

        /// <summary>
        /// Создаёт лимитер: слоты на хост и пороги вычистки берутся из опций краулера.
        /// </summary>
        /// <param name="options">Параметры краулера.</param>
        public HostRateLimiter(CrawlerOptions options)
        {
            _options = options;
        }

        /// <summary>Число хостов, хранимых лимитером.</summary>
        public int HostCount => _hosts.Count;

        /// <inheritdoc />
        public async Task<IDisposable> AcquireAsync(string host, CancellationToken cancellationToken = default)
        {
            PruneIfNeeded();

            HostEntry entry = Enter(host);
            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken);
            }
            catch
            {
                // Ожидание не завершилось захватом слота — откатываем только счётчик входов.
                Interlocked.Decrement(ref entry.RefCount);
                throw;
            }

            entry.LastUse = Environment.TickCount64;
            return new HostGate(entry);
        }

        private HostEntry Enter(string host)
        {
            while (true)
            {
                HostEntry entry = _hosts.GetOrAdd(host, static (_, max) => new HostEntry(max), _options.MaxConcurrentPerHost);

                // Входим в запись только атомарным подсчётом: вычистка помечает запись
                // значением -1 строго из состояния RefCount == 0, поэтому удерживаемая
                // или ожидаемая запись никогда не будет удалена.
                int seen;
                do
                {
                    seen = Volatile.Read(ref entry.RefCount);
                    if (seen < 0)
                    {
                        break;
                    }
                } while (Interlocked.CompareExchange(ref entry.RefCount, seen + 1, seen) != seen);

                if (seen < 0)
                {
                    continue;
                }

                if (_hosts.TryGetValue(host, out HostEntry? mapped) && ReferenceEquals(mapped, entry))
                {
                    return entry;
                }

                Interlocked.Decrement(ref entry.RefCount);
            }
        }

        private void PruneIfNeeded()
        {
            long now = Environment.TickCount64;
            long intervalMs = _options.HostPruneIntervalSeconds * 1000L;
            if (now - Volatile.Read(ref _lastPruneCheck) < intervalMs)
            {
                return;
            }

            Volatile.Write(ref _lastPruneCheck, now);

            if (_hosts.Count < _options.HostPruneThreshold)
            {
                return;
            }

            long idleMs = _options.HostIdleSeconds * 1000L;
            foreach (KeyValuePair<string, HostEntry> pair in _hosts)
            {
                HostEntry entry = pair.Value;
                if (now - Volatile.Read(ref entry.LastUse) <= idleMs)
                {
                    continue;
                }

                if (Interlocked.CompareExchange(ref entry.RefCount, -1, 0) == 0)
                {
                    _hosts.TryRemove(pair);
                }
            }
        }

        private sealed class HostEntry
        {
            public readonly SemaphoreSlim Semaphore;
            public long LastUse;
            public int RefCount;

            public HostEntry(int maxConcurrentPerHost)
            {
                Semaphore = new SemaphoreSlim(maxConcurrentPerHost);
                LastUse = Environment.TickCount64;
            }
        }

        private sealed class HostGate : IDisposable
        {
            private readonly HostEntry _entry;

            public HostGate(HostEntry entry)
            {
                _entry = entry;
            }

            public void Dispose()
            {
                _entry.Semaphore.Release();
                _entry.LastUse = Environment.TickCount64;
                Interlocked.Decrement(ref _entry.RefCount);
            }
        }
    }
}
