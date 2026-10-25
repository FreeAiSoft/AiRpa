using System;
using System.Text;

namespace RPA.VsTemplate
{
    /// <summary>Processes template text: evaluates $if$/$else$/$endif$ conditions and resolves $parameters$.</summary>
    public static class TemplateTextProcessor
    {
        public static string Process(string text, Func<string, string> resolveParameter)
        {
            if (text == null)
                return null;

            string result = ProcessConditions(text, resolveParameter);
            result = ReplaceParameters(result, resolveParameter);
            return result;
        }

        public static string ReplaceParameters(string text, Func<string, string> resolveParameter)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            int index = text.IndexOf('$');
            if (index < 0)
                return text;

            var builder = new StringBuilder(text.Length);
            int cursor = 0;
            while (index >= 0)
            {
                int close = text.IndexOf('$', index + 1);
                if (close < 0)
                    break;

                string name = text.Substring(index + 1, close - index - 1);
                builder.Append(text, cursor, index - cursor);
                builder.Append(resolveParameter(name));
                cursor = close + 1;
                index = text.IndexOf('$', cursor);
            }

            builder.Append(text, cursor, text.Length - cursor);
            return builder.ToString();
        }

        public static string ProcessConditions(string text, Func<string, string> resolveParameter)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            string current = text;
            while (true)
            {
                int ifPos = current.IndexOf("$if$", StringComparison.Ordinal);
                if (ifPos < 0)
                    break;

                // Locate the parenthesized condition expression after $if$.
                int parenStart = current.IndexOf('(', ifPos + 4);
                string expression;
                int contentStart;
                if (parenStart >= 0)
                {
                    int parenEnd = FindMatchingParen(current, parenStart);
                    if (parenEnd < 0)
                        break; // Malformed condition; stop processing.

                    expression = current.Substring(parenStart + 1, parenEnd - parenStart - 1);
                    contentStart = parenEnd + 1;
                }
                else
                {
                    expression = string.Empty;
                    contentStart = ifPos + 4;
                }

                // Find the matching $endif$ (accounting for nesting) and the $else$ at the top level.
                int elsePos = -1;
                int endPos = -1;
                int depth = 1;
                int scan = contentStart;
                while (scan < current.Length)
                {
                    int nextIf = current.IndexOf("$if$", scan, StringComparison.Ordinal);
                    int nextElse = current.IndexOf("$else$", scan, StringComparison.Ordinal);
                    int nextEnd = current.IndexOf("$endif$", scan, StringComparison.Ordinal);

                    int chosen = -1;
                    int min = int.MaxValue;
                    if (nextIf >= 0 && nextIf < min) { min = nextIf; chosen = 1; }
                    if (nextElse >= 0 && nextElse < min) { min = nextElse; chosen = 2; }
                    if (nextEnd >= 0 && nextEnd < min) { min = nextEnd; chosen = 3; }

                    if (chosen < 0)
                        break; // No closing token.

                    if (chosen == 1)
                    {
                        depth++;
                        scan = nextIf + 4;
                    }
                    else if (chosen == 2)
                    {
                        if (depth == 1)
                            elsePos = nextElse;
                        scan = nextElse + 6;
                    }
                    else
                    {
                        depth--;
                        if (depth == 0)
                        {
                            endPos = nextEnd;
                            break;
                        }
                        scan = nextEnd + 7;
                    }
                }

                if (endPos < 0)
                    break; // Malformed: no matching $endif$.

                bool condition = VsTemplateConditionEvaluator.Evaluate(expression, resolveParameter);

                int trueStart = contentStart;
                int trueEnd = elsePos >= 0 ? elsePos : endPos;
                string chosenText;
                if (condition)
                    chosenText = current.Substring(trueStart, trueEnd - trueStart);
                else if (elsePos >= 0)
                    chosenText = current.Substring(elsePos + 6, endPos - (elsePos + 6));
                else
                    chosenText = string.Empty;

                // Recursively process nested conditions inside the chosen branch.
                chosenText = ProcessConditions(chosenText, resolveParameter);

                current = current.Substring(0, ifPos) + chosenText + current.Substring(endPos + 7);
            }

            return current;
        }

        private static int FindMatchingParen(string text, int openIndex)
        {
            int depth = 0;
            for (int i = openIndex; i < text.Length; i++)
            {
                char character = text[i];
                if (character == '(')
                    depth++;
                else if (character == ')')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }
    }
}
