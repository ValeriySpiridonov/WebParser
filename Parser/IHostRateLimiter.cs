namespace WebParser.Parser
{
    /// <summary>
    /// Ограничивает число одновременных операций на хост.
    /// </summary>
    public interface IHostRateLimiter
    {
        /// <summary>
        /// Занимает слот для хоста, ожидая освобождения; возвращает дескриптор для его освобождения.
        /// </summary>
        /// <param name="host">Хост, для которого занимается слот.</param>
        /// <param name="cancellationToken">Токен отмены ожидания.</param>
        /// <returns>Дескриптор, освобождающий слот при освобождении.</returns>
        Task<IDisposable> AcquireAsync(string host, CancellationToken cancellationToken = default);
    }
}