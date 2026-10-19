namespace WebParser.Parser
{
    /// <summary>
    /// Пара «слово — частота»: слово в нижнем регистре и число вхождений в тексте.
    /// </summary>
    public readonly record struct WordFrequency(string Word, int Frequency)
    {
        /// <summary>
        /// Формирует строку представления пары для вывода пользователю.
        /// </summary>
        /// <param name="item">Пара «слово — частота».</param>
        /// <returns>Строка вида «Слово: …	Количество повторов: …».</returns>
        public static string FormatLine(WordFrequency item)
        {
            return $"Слово: {item.Word}\tКоличество повторов: {item.Frequency}";
        }
    }
}
