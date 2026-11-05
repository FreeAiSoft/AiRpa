using System;
using System.Collections.Generic;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Язык выражений процесса (карточка <c>AG-05</c>): от него зависит и проверка синтаксиса,
    /// и то, какие значения конструктор подставит в XAML (<c>CSharpValue&lt;T&gt;</c> /
    /// <c>VisualBasicValue&lt;T&gt;</c>).
    /// </summary>
    public enum WorkflowExpressionLanguage
    {
        CSharp,

        VisualBasic
    }

    /// <summary>
    /// Запрос «задай аргумент активности» (карточка <c>AG-05</c>).
    /// <para>
    /// Аргумент адресуется так же, как всё остальное: адрес элемента из <c>workflow_outline</c>
    /// плюс имя аргумента из <c>activity_describe</c>. Значение задаётся <b>либо</b> литералом
    /// (<see cref="Value"/>), <b>либо</b> выражением (<see cref="Expression"/>): два значения сразу —
    /// ошибка запроса, а не «последнее выиграло».
    /// </para>
    /// </summary>
    public sealed class WorkflowArgumentSetRequest
    {
        private bool _valueSpecified;

        /// <summary>Адрес активности из <c>workflow_outline</c> (<c>root/Activities[0]</c>).</summary>
        public string ElementId { get; set; }

        /// <summary>Имя аргумента (<c>Text</c>, <c>Condition</c>, <c>To</c>) — из <c>activity_describe</c>.</summary>
        public string Argument { get; set; }

        /// <summary>Литеральное значение в виде текста: <c>hello</c>, <c>42</c>, <c>true</c>.</summary>
        public string Value { get; private set; }

        /// <summary>Значение задано литералом (в том числе пустой строкой — это тоже литерал).</summary>
        public bool ValueSpecified => _valueSpecified;

        /// <summary>C#/VB-выражение (<c>output.ToUpper()</c>, <c>total + 1</c>).</summary>
        public string Expression { get; set; }

        public bool HasExpression => !string.IsNullOrWhiteSpace(Expression);

        /// <summary>
        /// Имя типа значения для необобщённых <c>InArgument</c>/<c>OutArgument</c>
        /// (<c>Assign.To</c>): <c>String</c>, <c>Int32</c>. У обычных аргументов тип известен каталогу.
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>Язык выражения (<c>csharp</c>/<c>vb</c>); пусто — определяется по документу.</summary>
        public string Language { get; set; }

        /// <summary>Задаёт литеральное значение (пустая строка — допустимый литерал).</summary>
        public WorkflowArgumentSetRequest SetValue(string value)
        {
            Value = value;
            _valueSpecified = true;
            return this;
        }

        public override string ToString()
        {
            string what = HasExpression
                ? "= " + Expression.Trim()
                : ValueSpecified ? "= " + (Value ?? "null") : "(значение не задано)";

            return (ElementId ?? string.Empty) + "." + (Argument ?? string.Empty) + " " + what;
        }
    }

    /// <summary>
    /// Запрос «добавь переменную» (карточка <c>AG-05</c>): переменная объявляется в активности, у которой
    /// есть коллекция <c>Variables</c> (<c>Sequence</c>, <c>Flowchart</c>, корень процесса).
    /// </summary>
    public sealed class WorkflowVariableAddRequest
    {
        private string _elementId;
        private bool _valueSpecified;

        /// <summary>Адрес владельца переменной; пусто — корень процесса.</summary>
        public string ElementId
        {
            get => string.IsNullOrWhiteSpace(_elementId) ? WorkflowEditing.RootElementId : _elementId.Trim();
            set => _elementId = value;
        }

        /// <summary>Имя переменной (идентификатор C#/VB).</summary>
        public string Name { get; set; }

        /// <summary>Имя типа (<c>String</c>, <c>Int32</c>) — из списка, который знает студия.</summary>
        public string TypeName { get; set; }

        /// <summary>Значение по умолчанию литералом.</summary>
        public string Value { get; private set; }

        public bool ValueSpecified => _valueSpecified;

        /// <summary>Значение по умолчанию выражением.</summary>
        public string Expression { get; set; }

        public bool HasExpression => !string.IsNullOrWhiteSpace(Expression);

        /// <summary>Язык выражения (<c>csharp</c>/<c>vb</c>); пусто — определяется по документу.</summary>
        public string Language { get; set; }

        public WorkflowVariableAddRequest SetValue(string value)
        {
            Value = value;
            _valueSpecified = true;
            return this;
        }

        public override string ToString()
        {
            return ElementId + ": " + (TypeName ?? "?") + " " + (Name ?? "?");
        }
    }

    /// <summary>Запрос «удали переменную» (карточка <c>AG-05</c>).</summary>
    public sealed class WorkflowVariableRemoveRequest
    {
        private string _elementId;

        /// <summary>Адрес владельца переменной; пусто — корень процесса.</summary>
        public string ElementId
        {
            get => string.IsNullOrWhiteSpace(_elementId) ? WorkflowEditing.RootElementId : _elementId.Trim();
            set => _elementId = value;
        }

        /// <summary>Имя удаляемой переменной.</summary>
        public string Name { get; set; }

        public override string ToString()
        {
            return ElementId + ": " + (Name ?? "?");
        }
    }

    /// <summary>
    /// Чистая проверка выражения (карточка <c>AG-05</c>): правила студии, которые не зависят от документа.
    /// <para>
    /// Здесь только то, что видно по тексту: пустое выражение, незакрытые скобки/кавычки, обрыв на
    /// операторе, лямбды (студия их не сериализует) и явная путаница языков (C#-документ с VB-синтаксисом).
    /// Глубокий разбор делает студия (<c>WorkflowExpressionSyntax</c> на Roslyn), а поиск неизвестных имён
    /// — штатная валидация WF после правки: до неё переменные в области видимости ещё не собраны.
    /// </para>
    /// </summary>
    public static class WorkflowExpressionPolicy
    {
        /// <summary>Предел длины выражения: длиннее — почти наверняка вставлен код, а не выражение.</summary>
        public const int MaxLength = 4000;

        /// <summary>Проблемы выражения; пустой список — проверка пройдена.</summary>
        public static IReadOnlyList<string> Check(string expression, WorkflowExpressionLanguage language)
        {
            var problems = new List<string>();
            string text = expression?.Trim();

            if (string.IsNullOrEmpty(text))
            {
                problems.Add("выражение не задано");
                return problems;
            }

            if (text.Length > MaxLength)
            {
                problems.Add("выражение длиннее " + MaxLength + " символов: в аргументе ожидается выражение, а не код");
            }

            if (language == WorkflowExpressionLanguage.VisualBasic)
            {
                if (Contains(text, "=>") || Contains(text, "&&") || Contains(text, "||") || Contains(text, "=="))
                {
                    problems.Add("выражение записано в синтаксисе C#, а документ на Visual Basic: " +
                                 "используйте AndAlso/OrElse/= или укажите language=csharp");
                }

                if (Contains(text, "function(") || Contains(text, "sub(") || Contains(text, "function (") || Contains(text, "sub ("))
                {
                    problems.Add("лямбды и делегаты запрещены: значение аргумента — выражение, а не функция");
                }
            }
            else
            {
                if (Contains(text, "=>"))
                {
                    problems.Add("лямбды и делегаты запрещены: значение аргумента — выражение, а не функция");
                }

                if (Contains(text, "andalso") || Contains(text, "orelse") || Contains(text, "nothing") || Contains(text, ":="))
                {
                    problems.Add("выражение записано в синтаксисе Visual Basic, а документ на C#: " +
                                 "используйте &&, ||, null или укажите language=vb");
                }
            }

            string balance = DescribeUnbalanced(text);
            if (balance != null)
            {
                problems.Add(balance);
            }

            char last = text[text.Length - 1];
            if ("+-*/%&|=<>,.".IndexOf(last) >= 0)
            {
                problems.Add("выражение обрывается на операторе «" + last + "»");
            }

            return problems;
        }

        /// <summary>Проблемы выражения, собранные в одну строку (для ответа инструмента).</summary>
        public static string Describe(IReadOnlyList<string> problems)
        {
            if (problems == null || problems.Count == 0)
            {
                return string.Empty;
            }

            return string.Join("; ", problems);
        }

        private static bool Contains(string text, string fragment)
        {
            return text.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Незакрытые скобки и кавычки: самая частая ошибка ручного ввода выражения.</summary>
        private static string DescribeUnbalanced(string text)
        {
            var stack = new Stack<char>();
            bool inString = false;
            bool inChar = false;

            for (int index = 0; index < text.Length; index++)
            {
                char symbol = text[index];

                if (inString)
                {
                    if (symbol == '\\' && index + 1 < text.Length)
                    {
                        index++;
                        continue;
                    }

                    if (symbol == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (inChar)
                {
                    if (symbol == '\\' && index + 1 < text.Length)
                    {
                        index++;
                        continue;
                    }

                    if (symbol == '\'')
                    {
                        inChar = false;
                    }

                    continue;
                }

                switch (symbol)
                {
                    case '"':
                        inString = true;
                        continue;
                    case '\'':
                        inChar = true;
                        continue;
                    case '(':
                    case '[':
                    case '{':
                        stack.Push(symbol);
                        continue;
                    case ')':
                    case ']':
                    case '}':
                    {
                        char expected = symbol == ')' ? '(' : symbol == ']' ? '[' : '{';

                        if (stack.Count == 0)
                        {
                            return "закрывающая скобка «" + symbol + "» без открывающей";
                        }

                        if (stack.Pop() != expected)
                        {
                            return "не совпадает пара скобок у «" + symbol + "»";
                        }

                        continue;
                    }
                }
            }

            if (inString)
            {
                return "не закрыта кавычка в выражении";
            }

            if (inChar)
            {
                return "не закрыта одинарная кавычка в выражении";
            }

            return stack.Count > 0 ? "не закрыта открывающая скобка «" + stack.Peek() + "»" : null;
        }
    }

    /// <summary>Имена языков выражений в запросах инструментов (<c>csharp</c>, <c>vb</c>).</summary>
    public static class WorkflowExpressionLanguages
    {
        /// <summary>Разбирает имя языка; пусто — <c>null</c> (определит студия по документу).</summary>
        public static bool TryParse(string value, out WorkflowExpressionLanguage? language)
        {
            language = null;
            string text = value?.Trim();

            if (string.IsNullOrEmpty(text))
            {
                return true;
            }

            switch (text.ToLowerInvariant())
            {
                case "c#":
                case "cs":
                case "csharp":
                    language = WorkflowExpressionLanguage.CSharp;
                    return true;
                case "vb":
                case "vbnet":
                case "vb.net":
                case "visualbasic":
                    language = WorkflowExpressionLanguage.VisualBasic;
                    return true;
                default:
                    return false;
            }
        }

        public static string NameOf(WorkflowExpressionLanguage language)
        {
            return language == WorkflowExpressionLanguage.VisualBasic ? "vb" : "csharp";
        }
    }
}
