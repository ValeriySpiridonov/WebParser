namespace WebParser.Parser
{
    /// <summary>
    /// Скачивает страницу по URI с автоматическими повторами; реализация владеет HTTP-клиентом
    /// и может быть освобождена через <see cref="IDisposable"/>.
    /// </summary>
    public interface IHttpFetcher : IDisposable
    {
        /// <summary>
        /// Скачивает страницу по URI и возвращает ответ с уже прочитанными заголовками.
        /// </summary>
        /// <param name="uri">Полный HTTP(S) адрес страницы.</param>
        /// <param name="cancellationToken">Токен отмены операции.</param>
        /// <returns>Ответ сервера; код ответа может быть не-успешным (обрабатывается вызывающим).</returns>
        Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken cancellationToken = default);
    }
}