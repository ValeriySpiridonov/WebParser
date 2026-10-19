namespace WebParser.Tests;

public class CliArgsTests
{
    [Fact]
    public void NoArgs_YieldsDefaults()
    {
        CliArgs? args = CliArgs.Parse([]);

        Assert.NotNull(args);
        Assert.False(args.Verbose);
        Assert.Null(args.UrlFile);
    }

    [Fact]
    public void VerboseFlag_IsRecognizedInAnyPosition()
    {
        CliArgs? first = CliArgs.Parse(["--verbose", "urls.txt"]);
        Assert.NotNull(first);
        Assert.True(first.Verbose);
        Assert.Equal("urls.txt", first.UrlFile);

        CliArgs? second = CliArgs.Parse(["urls.txt", "-v"]);
        Assert.NotNull(second);
        Assert.True(second.Verbose);
        Assert.Equal("urls.txt", second.UrlFile);
    }

    [Fact]
    public void UnknownOption_ReturnsNull()
    {
        Assert.Null(CliArgs.Parse(["--unknown"]));
    }
}
