using System;

namespace RPA.VsTemplate
{
    /// <summary>Evaluates $if$ (expression) conditions in templates.</summary>
    public static class VsTemplateConditionEvaluator
    {
        public static bool Evaluate(string expression, Func<string, string> resolveParameter)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return false;

            string expr = expression.Trim();

            // Resolve parameters inside the expression.
            expr = TemplateTextProcessor.ReplaceParameters(expr, resolveParameter);

            // Strip optional surrounding parentheses.
            while (expr.Length >= 2 && expr[0] == '(' && expr[expr.Length - 1] == ')')
                expr = expr.Substring(1, expr.Length - 2).Trim();

            if (string.IsNullOrEmpty(expr))
                return false;

            string op = FindOperator(expr);
            if (op == null)
            {
                // A bare value is treated as a boolean (true unless it is "false").
                return !expr.Equals("false", StringComparison.OrdinalIgnoreCase);
            }

            string[] parts = SplitOnOperator(expr, op);
            string left = StripQuotes(parts[0].Trim());
            string right = StripQuotes(parts[1].Trim());

            return Compare(left, right, op);
        }

        private static string FindOperator(string expr)
        {
            string[] operators = { ">=", "<=", "==", "!=", ">", "<" };
            foreach (string op in operators)
            {
                if (expr.IndexOf(op, StringComparison.Ordinal) >= 0)
                    return op;
            }

            return null;
        }

        private static string[] SplitOnOperator(string expr, string op)
        {
            int index = expr.IndexOf(op, StringComparison.Ordinal);
            string left = expr.Substring(0, index);
            string right = expr.Substring(index + op.Length);
            return new[] { left, right };
        }

        private static string StripQuotes(string value)
        {
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[value.Length - 1] == '"') ||
                 (value[0] == '\'' && value[value.Length - 1] == '\'')))
            {
                return value.Substring(1, value.Length - 2);
            }

            return value;
        }

        private static bool Compare(string left, string right, string op)
        {
            if (Version.TryParse(left, out Version leftVersion) && Version.TryParse(right, out Version rightVersion))
            {
                int comparison = leftVersion.CompareTo(rightVersion);
                switch (op)
                {
                    case ">=": return comparison >= 0;
                    case "<=": return comparison <= 0;
                    case ">": return comparison > 0;
                    case "<": return comparison < 0;
                    case "==": return comparison == 0;
                    case "!=": return comparison != 0;
                }
            }

            int ordinal = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
            switch (op)
            {
                case ">=": return ordinal >= 0;
                case "<=": return ordinal <= 0;
                case ">": return ordinal > 0;
                case "<": return ordinal < 0;
                case "==": return ordinal == 0;
                case "!=": return ordinal != 0;
                default: return false;
            }
        }
    }
}
