using System.Diagnostics.CodeAnalysis;

namespace WebParser.Parser
{
    /// <summary>
    /// Проверяет, что строка является полным HTTP(S) адресом.
    /// </summary>
    public static class UrlValidator
    {
        /// <summary>
        /// Пытается разобрать строку как полный HTTP(S) адрес.
        /// </summary>
        /// <param name="url">Проверяемая строка.</param>
        /// <param name="uri">Разобранный адрес; null, если строка не является HTTP(S) адресом.</param>
        /// <returns>True, если строка — корректный HTTP(S) адрес.</returns>
        public static bool TryParse(string url, [NotNullWhen(true)] out Uri? uri)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }
    }
}
