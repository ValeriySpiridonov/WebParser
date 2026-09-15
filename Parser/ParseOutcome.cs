namespace WebParser.Parser
{
    /// <summary>
    /// Исход обработки одного адреса.
    /// </summary>
    public enum ParseOutcome
    {
        /// <summary>Страница успешно скачана и обработана.</summary>
        Success,

        /// <summary>Адрес не является корректным HTTP(S) URL.</summary>
        InvalidUrl,

        /// <summary>Сервер вернул код ответа HTTP об ошибке.</summary>
        HttpError,

        /// <summary>Размер страницы превысил допустимый лимит.</summary>
        PageTooLarge,

        /// <summary>Обработка завершилась ошибкой.</summary>
        Failed
    }
}