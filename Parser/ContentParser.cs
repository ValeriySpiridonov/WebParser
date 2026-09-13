using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using WebParser.Logger;

namespace WebParser.Parser
{
    public class ContentParser : IContentParser
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private readonly ILogger _logger;

        static ContentParser()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public ContentParser(ILogger logger)
        {
            _logger = logger;
        }

        public async Task ParseAsync(string url)
        {
            try
            {
                _logger.Log(new LogMessage { Message = $"Обработка адреса {url}" });

                using var response = await Http.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.Log(new LogMessage
                    {
                        Message = $"Не удалось обработать адрес {url}. Попробуйте выполнить запрос позднее."
                    });
                    return;
                }

                Encoding encoding = GetEncoding(response);
                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream, encoding);

                string html = await reader.ReadToEndAsync();
                string text = ExtractText(html).ToUpperInvariant();

                char[] delimiterChars = { ' ', ',', '.', '!', '?', '"', ';', ':', '[', ']', '(', ')', '\n', '\r', '\t' };
                string[] words = text.Split(delimiterChars);
                var result = words.GroupBy(x => x)
                                  .Select(x => new { Word = x.Key, Frequency = x.Count() })
                                  .Where(x => x.Word.Length > 0)
                                  .OrderByDescending(x => x.Frequency);

                _logger.Log(new LogMessage { Message = $"Обработка адреса {url} завершена. Результаты обработки:" });
                foreach (var item in result)
                {
                    string msg = $"Слово: {item.Word}\tКоличество повторов: {item.Frequency}";
                    Console.WriteLine(msg);
                    _logger.Log(new LogMessage { Message = msg });
                }
            }
            catch (Exception ex)
            {
                _logger.Log(new LogMessage { Message = $"Ошибка при обработке адреса {url}", Exception = ex });
            }
        }

        private static string ExtractText(string html)
        {
            var document = new HtmlDocument();
            document.LoadHtml(html);

            foreach (var node in document.DocumentNode
                         .Descendants()
                         .Where(n => n.Name == "script" || n.Name == "style")
                         .ToList())
            {
                node.Remove();
            }

            return document.DocumentNode.InnerText;
        }

        private static Encoding GetEncoding(HttpResponseMessage response)
        {
            string charset = response.Content.Headers.ContentType?.CharSet;
            if (string.IsNullOrWhiteSpace(charset))
                return Encoding.UTF8;

            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                return Encoding.UTF8;
            }
        }
    }
}