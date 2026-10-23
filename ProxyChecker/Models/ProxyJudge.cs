using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProxyChecker.Models
{
    internal class ProxyJudge
    {
        public static Dictionary<string, string> Parse(string result)
        {
            Dictionary<string, string> values = new();

            foreach (Match match in Regex.Matches(result, @"[A-Z_]+ = [^\n]+").Cast<Match>())
            {
                string[] info = Regex.Split(match.Value, " = ");
                values.Add(info[0], info[1]);
            }

            return values;
        }
    }
}
