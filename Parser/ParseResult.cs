namespace WebParser.Parser
{
    public enum ParseOutcome
    {
        Success,
        InvalidUrl,
        HttpError,
        PageTooLarge,
        Failed
    }

    public readonly record struct ParseResult(ParseOutcome Outcome, IReadOnlyList<WordFrequency>? Words = null)
    {
        public bool Succeeded => Outcome == ParseOutcome.Success;
    }
}