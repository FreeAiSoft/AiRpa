using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;
using RPA.Chat.Services.Workflow;
using RPA.Helpers;

namespace RPA.Core.Workflow
{
    /// <summary>
    /// Синтаксическая проверка выражения аргумента (карточка <c>AG-05</c>) на Roslyn: C# и Visual Basic.
    /// <para>
    /// Проверяется только разбор (parse), без привязки имён: на момент правки переменные и аргументы
    /// процесса ещё не собраны, а «неизвестное имя» — это не синтаксис, а семантика, которую показывает
    /// штатная валидация WF после правки (<c>validate</c> в инструментах). Поэтому ложных отказов
    /// «имя не найдено» здесь нет, а опечатка вида <c>1 +</c> или незакрытая скобка ловится сразу.
    /// </para>
    /// <para>
    /// Класс headless: ни дизайнера, ни UI он не требует — выражение разбирается как текст, поэтому
    /// проверяется юнит-тестами.
    /// </para>
    /// </summary>
    internal static class WorkflowExpressionSyntax
    {
        /// <summary>Проблемы синтаксиса выражения; пустой список — выражение разбирается.</summary>
        internal static IReadOnlyList<string> Check(string expression, WorkflowExpressionLanguage language)
        {
            string text = expression?.Trim();

            if (string.IsNullOrEmpty(text))
            {
                return new[] { "выражение не задано" };
            }

            try
            {
                return language == WorkflowExpressionLanguage.VisualBasic
                    ? CheckVisualBasic(text)
                    : CheckCSharp(text);
            }
            catch (Exception ex)
            {
                // Разбор не должен ронять правку: текстовое сообщение полезнее исключения.
                return new[] { "выражение не разобрано: " + ex.Message };
            }
        }

        private static IReadOnlyList<string> CheckCSharp(string expression)
        {
            // Выражение оборачивается в метод: так разбор видит именно выражение и не падает
            // на «недописанном» коде (например, на `if (...)` — это ошибка запроса, а не синтаксиса).
            string code = "class __ExpressionProbe { object __Value() { return " + expression + "; } }";

            SyntaxTree tree = CSharpSyntaxTree.ParseText(
                code,
                CSharpParseOptions.Default.WithLanguageVersion(Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp7_3));

            return ErrorsOf(tree, expression);
        }

        private static IReadOnlyList<string> CheckVisualBasic(string expression)
        {
            string code = "Module __ExpressionProbe" + Environment.NewLine +
                          "Function __Value() As Object" + Environment.NewLine +
                          "Return " + expression + Environment.NewLine +
                          "End Function" + Environment.NewLine +
                          "End Module";

            SyntaxTree tree = VisualBasicSyntaxTree.ParseText(
                code,
                VisualBasicParseOptions.Default.WithLanguageVersion(Microsoft.CodeAnalysis.VisualBasic.LanguageVersion.VisualBasic15_5));

            return ErrorsOf(tree, expression);
        }

        private static IReadOnlyList<string> ErrorsOf(SyntaxTree tree, string expression)
        {
            var problems = new List<string>();

            foreach (Diagnostic diagnostic in tree.GetDiagnostics())
            {
                if (diagnostic.Severity != DiagnosticSeverity.Error)
                {
                    continue;
                }

                // Текст диагностики уже содержит место ошибки; предел нужен, чтобы ответ инструмента
                // не превращался в простыню на длинном выражении.
                string message = diagnostic.GetMessage();
                problems.Add("синтаксис выражения: " + message + " (выражение: " + Shorten(expression) + ")");

                if (problems.Count >= 5)
                {
                    break;
                }
            }

            return problems;
        }

        private static string Shorten(string text)
        {
            string trimmed = (text ?? string.Empty).Trim();
            return trimmed.Length <= 80 ? trimmed : trimmed.Substring(0, 77) + "...";
        }
    }

    /// <summary>
    /// Язык выражений документа (карточка <c>AG-05</c>): студия по умолчанию создаёт C#-процессы, но
    /// XAML может прийти с Visual Basic-выражениями (<c>VisualBasicValue</c>, <c>mv:</c> в корне).
    /// <para>
    /// Язык определяется по документу, а не по настройке: агент правит файл, а не студию, и явный
    /// <c>language</c> в запросе остаётся главнее — тогда агент сам отвечает за синтаксис.
    /// </para>
    /// </summary>
    internal static class WorkflowExpressionLanguageDetector
    {
        /// <summary>Язык выражения для запроса: явный <paramref name="requested"/> важнее документа.</summary>
        internal static WorkflowExpressionLanguage Resolve(string filePath, string requested)
        {
            if (WorkflowExpressionLanguages.TryParse(requested, out WorkflowExpressionLanguage? explicitLanguage)
                && explicitLanguage.HasValue)
            {
                return explicitLanguage.Value;
            }

            return OfFile(filePath);
        }

        /// <summary>Язык по тексту файла: VB выдаёт себя пространством имён Visual Basic.</summary>
        internal static WorkflowExpressionLanguage OfFile(string filePath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                {
                    return OfText(File.ReadAllText(filePath));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Язык документа не определён: " + ErrorText.Of(ex));
            }

            return WorkflowExpressionLanguage.CSharp;
        }

        internal static WorkflowExpressionLanguage OfText(string xaml)
        {
            string text = xaml ?? string.Empty;

            if (text.IndexOf("Microsoft.VisualBasic", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("VisualBasicValue", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("VisualBasicReference", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return WorkflowExpressionLanguage.VisualBasic;
            }

            return WorkflowExpressionLanguage.CSharp;
        }
    }
}
