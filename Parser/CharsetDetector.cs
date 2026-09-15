using System.Text;

namespace WebParser.Parser
{
    /// <summary>
    /// Определяет кодировку страницы по заголовку ответа и декларации в &lt;meta charset&gt;.
    /// </summary>
    public static class CharsetDetector
    {
        private static readonly byte[] MetaCharsetPattern = Encoding.ASCII.GetBytes("charset=");

        /// <summary>Размер префикса тела (в байтах), в котором ищется декларация кодировки.</summary>
        public const int DetectPrefixBytes = 4 * 1024;

        static CharsetDetector()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        /// <summary>
        /// Определяет кодировку: приоритет у заголовка ответа, затем &lt;meta charset&gt;, иначе UTF-8.
        /// </summary>
        /// <param name="body">Префикс тела страницы, в котором ищется декларация кодировки.</param>
        /// <param name="headerCharset">Значение charset из заголовка Content-Type.</param>
        /// <returns>Определённая кодировка страницы.</returns>
        public static Encoding Resolve(ReadOnlySpan<byte> body, string? headerCharset)
        {
            Encoding? encoding = GetEncodingSafe(headerCharset);
            if (encoding != null)
            {
                return encoding;
            }

            return GetEncodingFromMeta(body) ?? Encoding.UTF8;
        }

        private static Encoding? GetEncodingFromMeta(ReadOnlySpan<byte> body)
        {
            int marker = IndexOfCharsetPattern(body);
            if (marker < 0)
            {
                return null;
            }

            int start = marker + MetaCharsetPattern.Length;
            int end = Math.Min(body.Length, start + 128);
            ReadOnlySpan<byte> segment = body.Slice(start, end - start);

            int nameStart = 0;
            while (nameStart < segment.Length && (segment[nameStart] == (byte)'"' || segment[nameStart] == (byte)'\''))
            {
                nameStart++;
            }

            int nameEnd = nameStart;
            while (nameEnd < segment.Length && !IsDelimiter((char)segment[nameEnd]))
            {
                nameEnd++;
            }

            string name = Encoding.ASCII.GetString(segment.Slice(nameStart, nameEnd - nameStart)).Trim();
            return GetEncodingSafe(name);
        }

        private static int IndexOfCharsetPattern(ReadOnlySpan<byte> body)
        {
            ReadOnlySpan<byte> pattern = MetaCharsetPattern;
            for (int i = 0; i + pattern.Length <= body.Length; i++)
            {
                if (LowerAscii(body[i]) != pattern[0])
                {
                    continue;
                }

                bool matched = true;
                for (int j = 1; j < pattern.Length; j++)
                {
                    if (LowerAscii(body[i + j]) != pattern[j])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return i;
                }
            }

            return -1;
        }

        private static Encoding? GetEncodingSafe(string? charset)
        {
            if (string.IsNullOrWhiteSpace(charset))
            {
                return null;
            }

            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        private static byte LowerAscii(byte b)
        {
            return b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b | 0x20) : b;
        }

        private static bool IsDelimiter(char c)
        {
            return c == '"' || c == '\'' || c == '>' || c == ' ' || c == ';' || c == '\n' || c == '\r' || c == '\t' || c == '/';
        }
    }
}