namespace WebParser.Parser
{
    /// <summary>
    /// Слово и количество его вхождений в тексте страницы.
    /// </summary>
    public readonly record struct WordFrequency(string Word, int Frequency);
}