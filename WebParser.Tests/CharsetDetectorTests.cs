using System.Text;
using WebParser.Parser;

namespace WebParser.Tests;

public class CharsetDetectorTests
{
    static CharsetDetectorTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static Encoding Resolve(string html, string? headerCharset = null)
    {
        return CharsetDetector.Resolve(Encoding.UTF8.GetBytes(html), headerCharset);
    }

    [Fact]
    public void HeaderCharset_TakesPriorityOverMeta()
    {
        Assert.Equal(Encoding.UTF8, Resolve("<meta charset='windows-1251'>", "utf-8"));
    }

    [Theory]
    [InlineData("<meta charset=\"windows-1251\">", "windows-1251")]
    [InlineData("<meta charset='koi8-r'>", "koi8-r")]
    [InlineData("<meta charset=iso-8859-5>", "iso-8859-5")]
    [InlineData("<meta charset=windows-1251/>", "windows-1251")]
    [InlineData("<META CHARSET=windows-1251>", "windows-1251")]
    public void MetaCharset_IsUsed_WithoutHeader(string html, string expected)
    {
        Assert.Equal(Encoding.GetEncoding(expected), Resolve(html));
    }

    [Fact]
    public void UnknownHeaderCharset_FallsBackToMeta()
    {
        Assert.Equal(Encoding.GetEncoding("windows-1251"), Resolve("<meta charset='windows-1251'>", "bogus-enc"));
    }

    [Fact]
    public void EmptyHeaderCharset_FallsBackToMeta()
    {
        Assert.Equal(Encoding.GetEncoding("koi8-r"), Resolve("<meta charset='koi8-r'>", " "));
    }

    [Fact]
    public void NoCharsetDeclared_ReturnsUtf8()
    {
        Assert.Equal(Encoding.UTF8, Resolve("<html><body><p>Привет</p></body></html>"));
    }
}