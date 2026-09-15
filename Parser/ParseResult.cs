namespace WebParser.Parser
{
    /// <summary>
    /// Результат обработки одного адреса: исход и список слов с частотами.
    /// </summary>
    public readonly record struct ParseResult(ParseOutcome Outcome, IReadOnlyList<WordFrequency>? Words = null)
    {
        /// <summary>True, если адрес обработан успешно.</summary>
        public bool Succeeded => Outcome == ParseOutcome.Success;
    }
}