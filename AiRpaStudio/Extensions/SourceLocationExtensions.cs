using System.Activities.Debugger;

namespace RPA.Extensions
{
    public static class SourceLocationExtensions
    {
        public static string GetLogString(this SourceLocation sl)
        {
            return $" LT: {sl.StartLine} {sl.StartColumn}; RB: {sl.EndLine} {sl.EndColumn}; file: {sl.FileName}";
        }
    }
}
