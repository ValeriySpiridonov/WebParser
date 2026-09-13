using System.Threading.Tasks;

namespace WebParser.Parser
{
    public interface IContentParser
    {
        Task ParseAsync(string url);
    }
}