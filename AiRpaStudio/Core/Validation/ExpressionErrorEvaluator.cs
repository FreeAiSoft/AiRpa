using System;
using System.Collections.Generic;
using System.Threading;

namespace RPA.Core.Services
{
    /// <summary>
    /// Компилятор выражений: то немногое, что нужно проверке от Roslyn.
    /// <para>
    /// Интерфейс отделяет «дорогую» компиляцию от правил сравнения типов: правила — чистая логика
    /// (<see cref="ExpressionErrorEvaluator"/>), которую можно проверить юнит-тестами без WF и Roslyn.
    /// </para>
    /// </summary>
    internal interface IExpressionCompiler
    {
        /// <summary>
        /// Компилирует выражение в окружении и возвращает выведенный тип и ошибки компиляции.
        /// Вызывается из фоновой задачи: реализация обязана быть потокобезопасной.
        /// </summary>
        (Type Type, List<string> Errors) Check(string expressionText, ExpressionEnvironment environment);
    }

    /// <summary>
    /// Правила «выражение аргумента корректно» — перенесённая из <c>DesignerValidator</c> логика,
    /// освобождённая от <c>ModelItem</c>.
    /// <para>
    /// Раньше эти правила жили в <c>DesignerValidator.GetArgumentErrors</c> и работали прямо на модели:
    /// прочитать модель и скомпилировать выражение Roslyn означало одно и то же место на UI-потоке.
    /// Теперь модель читается на UI-потоке (<see cref="ActivityExpressionCheck"/>), а сравнение типов
    /// и компиляция выполняются здесь — в фоне, по адресам.
    /// </para>
    /// <para>
    /// Сообщения сохранены дословно: на них опираются и подсказка иконки, и ожидания тестов.
    /// Одно осознанное отличие от прежнего кода: сравнение типов литерала выполняется, даже если
    /// ошибка нашлась у другого аргумента той же активности (раньше проверка пропускалась из-за
    /// общего списка ошибок — замечание терялось).
    /// </para>
    /// </summary>
    internal static class ExpressionErrorEvaluator
    {
        /// <summary>
        /// Проверяет снимки выражений и возвращает замечания с адресами.
        /// <paramref name="cancellation"/> проверяется между проверками: устаревший проход
        /// (пользователь успел изменить документ) обязан остановиться, а не досчитывать всё.
        /// </summary>
        internal static IReadOnlyList<ActivityValidationError> Evaluate(
            IEnumerable<ActivityExpressionCheck> checks,
            IExpressionCompiler compiler,
            CancellationToken cancellation = default)
        {
            List<ActivityValidationError> errors = new List<ActivityValidationError>();

            if (checks == null)
            {
                return errors;
            }

            if (compiler == null)
            {
                throw new ArgumentNullException(nameof(compiler));
            }

            foreach (ActivityExpressionCheck check in checks)
            {
                if (cancellation.IsCancellationRequested)
                {
                    break;
                }

                if (check == null)
                {
                    continue;
                }

                foreach (string message in EvaluateCheck(check, compiler))
                {
                    // Аргумент и «чистый» текст переносятся отдельно: по ним вердикт доводится до поля выражения.
                    // Полная строка Message остаётся подсказкой иконки активности («аргумент: текст»).
                    errors.Add(new ActivityValidationError(
                        check.Address,
                        ComposeMessage(check.ArgumentName, message),
                        check.ArgumentName,
                        message));
                }
            }

            return errors;
        }

        /// <summary>Строка замечания: «аргумент: текст» (формат прежнего <c>DesignerValidator</c>).</summary>
        internal static string ComposeMessage(string argumentName, string message)
        {
            return string.IsNullOrEmpty(argumentName) ? message ?? string.Empty : argumentName + ": " + message;
        }

        private static List<string> EvaluateCheck(ActivityExpressionCheck check, IExpressionCompiler compiler)
        {
            List<string> messages = new List<string>();

            switch (check.Kind)
            {
                case ExpressionCheckKind.OutArgument:
                    if (!check.Environment.Contains(check.ExpressionText))
                    {
                        messages.Add($"{check.ExpressionText} не существует в текущем контексте.");
                    }

                    break;

                case ExpressionCheckKind.Literal:
                    EvaluateLiteral(check, compiler, messages);
                    break;

                default:
                    EvaluateExpression(check, compiler, messages);
                    break;
            }

            return messages;
        }

        private static void EvaluateLiteral(
            ActivityExpressionCheck check,
            IExpressionCompiler compiler,
            List<string> messages)
        {
            (Type literalExpressionType, List<string> literalErrors) = SafeCheck(compiler, check.CompileText, check.Environment);
            Append(messages, literalErrors);

            if (messages.Count > 0)
            {
                return;
            }

            if (check.LiteralType != null && check.LiteralType != check.ArgumentType)
            {
                messages.Add($"тип литерала {check.LiteralType} не соответствует типу аргумента {check.ArgumentType}");
            }

            if (literalExpressionType != null && literalExpressionType != check.ArgumentType)
            {
                messages.Add(
                    $"тип литерала {literalExpressionType} (выведенный компилятором) не соответствует типу аргумента {check.ArgumentType}");
            }
        }

        private static void EvaluateExpression(
            ActivityExpressionCheck check,
            IExpressionCompiler compiler,
            List<string> messages)
        {
            (Type expressionType, List<string> compilationErrors) = SafeCheck(compiler, check.CompileText, check.Environment);

            if (expressionType != null)
            {
                if (check.Kind == ExpressionCheckKind.AssignValue)
                {
                    EvaluateAssign(check, expressionType, messages);
                }
                else if (check.ArgumentType != null && !check.ArgumentType.IsAssignableFrom(expressionType))
                {
                    messages.Add($"{expressionType} невозможно присвоить типу {check.ArgumentType}.");
                }
            }

            Append(messages, compilationErrors);
        }

        private static void EvaluateAssign(ActivityExpressionCheck check, Type expressionType, List<string> messages)
        {
            // Правая часть Assign — любое C#-выражение: литерал, вызов метода, обращение к члену,
            // оператор. «Не существует в контексте» говорится только про голый идентификатор, которого
            // нет ни среди переменных, ни среди аргументов: иначе литерал «"Hello from RPA-1"» помечается
            // как несуществующее имя.
            if (IsBareIdentifier(check.ExpressionText)
                && !check.Environment.Contains(check.ExpressionText))
            {
                messages.Add($"{check.ExpressionText} не существует в текущем контексте.");
                return;
            }

            if (expressionType != null
                && check.TargetType != null
                && !check.TargetType.IsAssignableFrom(expressionType))
            {
                messages.Add($"не возможно присвоить тип {expressionType} переменной типа {check.TargetType}.");
            }
        }

        /// <summary>
        /// «Голый идентификатор»: строка, которая может быть ссылкой на переменную или аргумент
        /// (<c>output</c>). Литералы (<c>"text"</c>), ключевые слова (<c>true</c>, <c>false</c>,
        /// <c>null</c>) и любые составные выражения (<c>output + "!"</c>, <c>output.Length</c>) сюда не
        /// попадают: для них сообщение «не существует в контексте» — ложная диагностика.
        /// </summary>
        private static bool IsBareIdentifier(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string name = text.Trim();

            if (name == "true" || name == "false" || name == "null"
                || name == "this" || name == "base")
            {
                return false;
            }

            if (name.StartsWith("@", StringComparison.Ordinal))
            {
                name = name.Substring(1);
            }

            if (name.Length == 0 || char.IsDigit(name[0]))
            {
                return false;
            }

            foreach (char symbol in name)
            {
                if (!char.IsLetterOrDigit(symbol) && symbol != '_')
                {
                    return false;
                }
            }

            return true;
        }

        private static (Type Type, List<string> Errors) SafeCheck(
            IExpressionCompiler compiler,
            string expressionText,
            ExpressionEnvironment environment)
        {
            try
            {
                return compiler.Check(expressionText ?? string.Empty, environment ?? ExpressionEnvironment.Empty);
            }
            catch (Exception ex)
            {
                // Падение компилятора — это замечание к выражению, а не повод потерять весь проход.
                return (null, new List<string> { "не удалось проверить выражение: " + ex.Message });
            }
        }

        private static void Append(List<string> messages, List<string> errors)
        {
            if (errors == null)
            {
                return;
            }

            foreach (string error in errors)
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    messages.Add(error);
                }
            }
        }
    }
}
