using System.Threading;
using System.Threading.Tasks;

namespace WebParser.Parser
{
    public interface IContentParser
    {
        Task<ParseResult> ParseAsync(string url, CancellationToken cancellationToken = default);
    }
}