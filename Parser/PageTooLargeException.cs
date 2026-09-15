namespace WebParser.Parser
{
    /// <summary>
    /// Исключение, возникающее при превышении лимита размера скачиваемой страницы.
    /// </summary>
    public sealed class PageTooLargeException : Exception
    {
        /// <summary>Максимально допустимый размер страницы в байтах.</summary>
        public int MaxBytes { get; }

        /// <summary>
        /// Создаёт исключение с указанием превышенного лимита.
        /// </summary>
        /// <param name="maxBytes">Максимально допустимый размер страницы в байтах.</param>
        public PageTooLargeException(int maxBytes)
            : base($"Размер страницы превышает лимит в {maxBytes} байт")
        {
            MaxBytes = maxBytes;
        }
    }
}