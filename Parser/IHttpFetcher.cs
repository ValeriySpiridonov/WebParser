namespace WebParser.Parser
{
    /// <summary>
    /// Скачивает страницу по URI с автоматическими повторами; ответ с неуспешным кодом
    /// возвращается вызывающему и обрабатывается им.
    /// </summary>
    public interface IHttpFetcher
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