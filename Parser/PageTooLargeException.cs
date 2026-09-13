using System;

namespace WebParser.Parser
{
    public sealed class PageTooLargeException : Exception
    {
        public long MaxBytes { get; }

        public PageTooLargeException(long maxBytes)
            : base($"Размер страницы превышает лимит в {maxBytes} байт")
        {
            MaxBytes = maxBytes;
        }
    }
}