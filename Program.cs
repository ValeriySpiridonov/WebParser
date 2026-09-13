using System;
using System.Threading.Tasks;
using WebParser.Logger;
using WebParser.Parser;

namespace WebParser
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Введите адрес сайта для подсчета уникальных слов");
            string urlAddress = Console.ReadLine();
            urlAddress = string.IsNullOrWhiteSpace(urlAddress) ? "https://yandex.ru" : urlAddress;

            if (!Uri.TryCreate(urlAddress, UriKind.Absolute, out var uri) ||
                !(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                Console.Error.WriteLine($"Некорректный адрес: {urlAddress}");
                return;
            }

            var parser = new ContentParser(new DisposableFileLogger());
            await parser.ParseAsync(urlAddress);
            Console.ReadKey();
        }
    }
}