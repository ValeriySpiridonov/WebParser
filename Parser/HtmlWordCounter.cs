using System.Globalization;
using System.Text;

namespace WebParser.Parser
{
    public readonly record struct WordFrequency(string Word, int Frequency);

    public sealed class HtmlWordCounter
    {
        private static readonly HashSet<string> BlockTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "p", "br", "div", "h1", "h2", "h3", "h4", "h5", "h6",
            "ul", "ol", "li", "dl", "dt", "dd",
            "table", "thead", "tbody", "tfoot", "tr", "td", "th", "caption",
            "section", "article", "nav", "aside", "header", "footer", "main",
            "figure", "figcaption", "blockquote", "hr", "pre", "fieldset", "form",
            "address", "details", "summary", "select", "option"
        };

        private static readonly HashSet<string> SkipTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "script", "style", "textarea"
        };

        private static readonly HashSet<char> Delimiters = new HashSet<char>(" ,.!?\";:[]()\n\r\t");

        private enum Mode
        {
            Text,
            Tag,
            Comment,
            Skip
        }

        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly StringBuilder _word = new StringBuilder(32);
        private readonly StringBuilder _tagName = new StringBuilder(32);
        private readonly char[] _entityBuffer = new char[12];
        private int _entityLen;

        private Mode _mode;
        private bool _inQuotes;
        private char _quoteChar;
        private bool _closingTag;
        private bool _tagNameDone;
        private bool _fromSkip;
        private string? _skipTagName;
        private bool _separatorNeeded;
        private bool _skipLtPending;
        private bool _inEntity;
        private char _prev1;
        private char _prev2;

        public void Process(ReadOnlySpan<char> chars)
        {
            foreach (char c in chars)
            {
                ProcessChar(c);
            }
        }

        public IReadOnlyList<WordFrequency> Complete()
        {
            FlushWord();
            return _counts
                .Select(kv => new WordFrequency(kv.Key, kv.Value))
                .OrderByDescending(f => f.Frequency)
                .ThenBy(f => f.Word, StringComparer.Ordinal)
                .ToList();
        }

        private void ProcessChar(char c)
        {
            switch (_mode)
            {
                case Mode.Text:
                    ProcessTextChar(c);
                    break;
                case Mode.Tag:
                    ProcessTagChar(c);
                    break;
                case Mode.Comment:
                    ProcessCommentChar(c);
                    break;
                case Mode.Skip:
                    ProcessSkipChar(c);
                    break;
            }
        }

        private void ProcessTextChar(char c)
        {
            if (_separatorNeeded)
            {
                _separatorNeeded = false;
                FlushWord();
            }

            if (_inEntity)
            {
                ProcessEntityChar(c);
                return;
            }

            if (c == '<')
            {
                StartTag(fromSkip: false);
            }
            else if (c == '&')
            {
                _inEntity = true;
                _entityLen = 0;
            }
            else if (IsWordChar(c))
            {
                _word.Append(char.ToLowerInvariant(c));
            }
            else
            {
                FlushWord();
            }
        }

        private void ProcessEntityChar(char c)
        {
            if (c == ';')
            {
                char? resolved = ResolveEntity(_entityBuffer.AsSpan(0, _entityLen));
                _entityLen = 0;
                _inEntity = false;

                if (resolved.HasValue)
                {
                    FeedResolvedChar(resolved.Value);
                }
                else
                {
                    FlushWord();
                }

                return;
            }

            if (_entityLen >= 12)
            {
                _inEntity = false;
                _entityLen = 0;
                FlushWord();
                ProcessTextChar(c);
                return;
            }

            if (c != '#' && !char.IsLetterOrDigit(c))
            {
                _inEntity = false;
                _entityLen = 0;
                FlushWord();
                ProcessTextChar(c);
                return;
            }

            _entityBuffer[_entityLen++] = c;
        }

        private void FeedResolvedChar(char c)
        {
            if (char.IsLetterOrDigit(c))
            {
                _word.Append(char.ToLowerInvariant(c));
            }
            else
            {
                FlushWord();
            }
        }

        private static char? ResolveEntity(ReadOnlySpan<char> entity)
        {
            if (entity.IsEmpty)
            {
                return null;
            }

            if (entity[0] == '#')
            {
                ReadOnlySpan<char> digits = entity.Slice(1);
                NumberStyles style = NumberStyles.None;
                if (digits.Length > 0 && (digits[0] == 'x' || digits[0] == 'X'))
                {
                    digits = digits.Slice(1);
                    style = NumberStyles.HexNumber;
                }

                if (!int.TryParse(digits, style, CultureInfo.InvariantCulture, out int codePoint))
                {
                    return null;
                }

                if (codePoint <= 0 || codePoint > 0xFFFF)
                {
                    return null;
                }

                return (char)codePoint;
            }

            return entity.Equals("nbsp".AsSpan(), StringComparison.OrdinalIgnoreCase) ? ' ' :
                   entity.Equals("amp".AsSpan(), StringComparison.OrdinalIgnoreCase) ? '&' :
                   entity.Equals("lt".AsSpan(), StringComparison.OrdinalIgnoreCase) ? '<' :
                   entity.Equals("gt".AsSpan(), StringComparison.OrdinalIgnoreCase) ? '>' :
                   entity.Equals("quot".AsSpan(), StringComparison.OrdinalIgnoreCase) ? '"' :
                   entity.Equals("apos".AsSpan(), StringComparison.OrdinalIgnoreCase) ? '\'' :
                   null;
        }

        private void StartTag(bool fromSkip)
        {
            _fromSkip = fromSkip;
            _mode = Mode.Tag;
            _tagName.Clear();
            _tagNameDone = false;
            _closingTag = false;
            _inQuotes = false;
            _skipLtPending = false;
        }

        private void ProcessTagChar(char c)
        {
            if (_fromSkip && c == '<')
            {
                _mode = Mode.Skip;
                ResetTag();
                ProcessSkipChar(c);
                return;
            }

            if (_inQuotes && !_fromSkip)
            {
                if (c == _quoteChar)
                {
                    _inQuotes = false;
                }

                return;
            }

            if (!_tagNameDone)
            {
                if (c == '/' && _tagName.Length == 0)
                {
                    _closingTag = true;
                    return;
                }

                if (c == '\'' || c == '"')
                {
                    if (!_fromSkip)
                    {
                        _inQuotes = true;
                        _quoteChar = c;
                    }

                    _tagNameDone = true;
                    return;
                }

                if (c == '>')
                {
                    _tagNameDone = true;
                    EndTag();
                    return;
                }

                if (char.IsWhiteSpace(c))
                {
                    _tagNameDone = true;
                    return;
                }

                _tagName.Append(c);

                if (_tagName.Length == 3 && _tagName[0] == '!' && _tagName[1] == '-' && _tagName[2] == '-')
                {
                    _mode = Mode.Comment;
                    _prev1 = '\0';
                    _prev2 = '\0';
                }

                return;
            }

            if (!_fromSkip && (c == '\'' || c == '"'))
            {
                _inQuotes = true;
                _quoteChar = c;
                return;
            }

            if (c == '>')
            {
                EndTag();
            }
        }

        private void ProcessCommentChar(char c)
        {
            if (c == '>' && _prev1 == '-' && _prev2 == '-')
            {
                _mode = Mode.Text;
                _prev1 = '\0';
                _prev2 = '\0';
                return;
            }

            _prev2 = _prev1;
            _prev1 = c;
        }

        private void ProcessSkipChar(char c)
        {
            if (_skipLtPending)
            {
                _skipLtPending = false;

                if (c == '/')
                {
                    StartTag(fromSkip: true);
                    _closingTag = true;
                }

                return;
            }

            if (c == '<')
            {
                _skipLtPending = true;
            }
        }

        private void EndTag()
        {
            string name = _tagName.ToString();

            if (_fromSkip)
            {
                if (_closingTag && string.Equals(name, _skipTagName, StringComparison.OrdinalIgnoreCase))
                {
                    _skipTagName = null;
                    _mode = Mode.Text;
                }
                else
                {
                    _mode = Mode.Skip;
                }

                ResetTag();
                return;
            }

            if (name.Length > 0 && name[0] != '!' && name[0] != '?')
            {
                if (SkipTags.Contains(name))
                {
                    if (!_closingTag)
                    {
                        _skipTagName = name;
                        _mode = Mode.Skip;
                        ResetTag();
                        return;
                    }
                }
                else if (BlockTags.Contains(name))
                {
                    _separatorNeeded = true;
                }
            }

            _mode = Mode.Text;
            ResetTag();
        }

        private void ResetTag()
        {
            _fromSkip = false;
            _tagNameDone = false;
            _inQuotes = false;
            _closingTag = false;
        }

        private void FlushWord()
        {
            if (_word.Length == 0)
            {
                return;
            }

            string key = _word.ToString();
            _counts.TryGetValue(key, out int count);
            _counts[key] = count + 1;
            _word.Clear();
        }

        private bool IsWordChar(char c)
        {
            return !Delimiters.Contains(c);
        }
    }
}