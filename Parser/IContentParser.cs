namespace WebParser.Parser
{
    /// <summary>
    /// Обрабатывает одиночный адрес: скачивает страницу и считает частоту слов.
    /// </summary>
    public interface IContentParser
    {
        /// <summary>
        /// Скачивает страницу по адресу и возвращает результат разбора.
        /// </summary>
        /// <param name="url">Полный HTTP(S) адрес страницы.</param>
        /// <param name="cancellationToken">Токен отмены операции.</param>
        /// <returns>Результат обработки адреса.</returns>
        Task<ParseResult> ParseAsync(string url, CancellationToken cancellationToken = default);
    }
}