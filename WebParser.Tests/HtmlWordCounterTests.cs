using WebParser.Parser;

namespace WebParser.Tests;

public class HtmlWordCounterTests
{
    private static WordFrequency[] Count(string html)
    {
        var counter = new HtmlWordCounter();
        counter.Process(html.AsSpan());
        return counter.Complete().ToArray();
    }

    private static string[] Words(string html) => Count(html).Select(f => f.Word).ToArray();

    [Theory]
    [InlineData("<p>Example Domain</p><p>This domain</p>", new[] { "domain", "example", "this" })]
    [InlineData("<p>Мир привет</p><p>мир</p>", new[] { "мир", "привет" })]
    [InlineData("Hello world", new[] { "hello", "world" })]
    [InlineData("  spaced   out  ", new[] { "out", "spaced" })]
    [InlineData("line1\r\nline2\nline3", new[] { "line1", "line2", "line3" })]
    public void TextAndBlockSeparators(string html, string[] expected)
    {
        Assert.Equal(expected, Words(html));
    }

    [Theory]
    [InlineData("<span>Hello</span><span>World</span>", new[] { "helloworld" })]
    [InlineData("<div>a</div><div>b</div>", new[] { "a", "b" })]
    [InlineData("<h1>Title</h1><h2>Sub</h2>", new[] { "sub", "title" })]
    public void InlineNodes_AreJoined_UnlessBlock(string html, string[] expected)
    {
        Assert.Equal(expected, Words(html));
    }

    [Theory]
    [InlineData("<script>var x = 1;</script>visible", new[] { "visible" })]
    [InlineData("<script>document.write(\"</div>\");</script>text", new[] { "text" })]
    [InlineData("<script>if(a){document.write(\"<script>\")}</script>after", new[] { "after" })]
    [InlineData("<style>.a { color: red; }</style>styled", new[] { "styled" })]
    [InlineData("<textarea>not words</textarea>area", new[] { "area" })]
    [InlineData("<!-- comment <p>ignored</p> -->real", new[] { "real" })]
    [InlineData("<!DOCTYPE html><html><body>hi</body></html>", new[] { "hi" })]
    public void SkipTagsAndComments(string html, string[] expected)
    {
        Assert.Equal(expected, Words(html));
    }

    [Theory]
    [InlineData("<p>Tom &amp; Jerry</p>", new[] { "jerry", "tom" })]
    [InlineData("<p>&#104;&#101;&#108;&#108;&#111;</p>", new[] { "hello" })]
    [InlineData("<p>&#x48;&#x45;&#x4C;&#x4C;&#x4F;</p>", new[] { "hello" })]
    [InlineData("<p>Hello&nbsp;World</p>", new[] { "hello", "world" })]
    public void Entities(string html, string[] expected)
    {
        Assert.Equal(expected, Words(html));
    }

    [Fact]
    public void BareAmpersand_IsTreatedAsDelimiter()
    {
        Assert.Equal(new[] { "jerry", "tom" }, Words("Tom & Jerry"));
    }

    [Fact]
    public void Words_AreLowercased_AndCounted()
    {
        Assert.Equal(new[] { new WordFrequency("hello", 3) }, Count("Hello HELLO hEllO"));
    }

    [Fact]
    public void CyrillicWords_AreCounted()
    {
        Assert.Equal(new[] { new WordFrequency("привет", 2) }, Count("Привет привет"));
    }

    [Fact]
    public void Punctuation_IsTrimmed()
    {
        Assert.Equal(new[] { "are", "hello", "how", "world", "you" }, Words("Hello, world! How (are) you?"));
    }

    [Fact]
    public void QuoteInsideTag_DoesNotBreakParsing()
    {
        Assert.Equal(new[] { "link" }, Words("<a href=\"/x?a=1&b=2\" title=\">\">link</a>"));
    }

    [Fact]
    public void Complete_SortsByFrequencyThenWord()
    {
        var result = Count("a b a c b a");
        Assert.Equal(new[] { new WordFrequency("a", 3), new WordFrequency("b", 2), new WordFrequency("c", 1) }, result);
    }

    [Fact]
    public void ChunkedProcessing_MatchesWholeInput()
    {
        string html = "<p>Государство &amp; право</p><ul><li>список</li></ul><!-- x --><script>var a=1;</script>Tail";
        string[] expected = Words(html);

        var counter = new HtmlWordCounter();
        for (int i = 0; i < html.Length; i++)
        {
            counter.Process(html.AsSpan(i, 1));
        }
        string[] actual = counter.Complete().Select(f => f.Word).ToArray();

        Assert.Equal(expected, actual);
    }
}