namespace WebParser.Parser
{
    public sealed class PageTooLargeException : Exception
    {
        public int MaxBytes { get; }

        public PageTooLargeException(int maxBytes)
            : base($"Размер страницы превышает лимит в {maxBytes} байт")
        {
            MaxBytes = maxBytes;
        }
    }
}