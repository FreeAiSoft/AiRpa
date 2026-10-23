using System.Linq;

namespace Activities.Extensions
{
    public static class StringExtensions
    {
        public static string[] GetArgsArray(this string args)
        {
            return args?.Split(' ').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        }
    }
}
