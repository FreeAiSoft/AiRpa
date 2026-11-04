using System;
using System.Activities;
using System.Activities.Expressions;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using RPA.Helpers;

namespace RPA.Core.Generation
{
    /// <summary>Отчёт правила «нет лямбд в аргументах»: ошибки и подсказка, как исправить.</summary>
    internal sealed class XamlExpressionPolicyReport
    {
        /// <summary>Лямбд в аргументах нет — модель можно сериализовать.</summary>
        internal bool Ok => Violations.Count == 0;

        /// <summary>Найденные нарушения: аргумент, активность и тип лямбды.</summary>
        internal List<string> Violations { get; } = new List<string>();

        /// <summary>Подсказка «чем заменить лямбду».</summary>
        internal List<string> Hints { get; } = new List<string>();

        internal void AddViolation(string violation)
        {
            if (!string.IsNullOrWhiteSpace(violation))
            {
                Violations.Add(violation);
            }
        }

        internal void AddHint(string hint)
        {
            if (!string.IsNullOrWhiteSpace(hint) && !Hints.Contains(hint))
            {
                Hints.Add(hint);
            }
        }

        /// <summary>Текст отчёта для сообщения об ошибке теста или ответа инструмента.</summary>
        internal string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine(Ok ? "выражения: ок" : "выражения: ОШИБКИ");
            text.AppendLine($"нарушений: {Violations.Count}");

            foreach (string violation in Violations)
            {
                text.AppendLine("  - " + violation);
            }

            foreach (string hint in Hints)
            {
                text.AppendLine("  подсказка: " + hint);
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Правило «нет лямбд в аргументах» (п. 1.2 и 5 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <para>
    /// <c>new InArgument&lt;int&gt;(ctx =&gt; 5)</c> создаёт <see cref="LambdaValue{T}"/>, а
    /// <c>new OutArgument&lt;int&gt;(new LambdaReference&lt;int&gt;(...))</c> — <see cref="LambdaReference{T}"/>.
    /// Ни то, ни другое не переживает сериализацию: в XAML остаётся пустой тег, конструктор показывает
    /// невалидный узел, а процесс после повторной загрузки падает. Поэтому проверка выполняется
    /// <b>до</b> сохранения — по объектной модели, а не по тексту XAML.
    /// </para>
    /// <para>
    /// Разрешены только <c>CSharpValue&lt;T&gt;</c> и <c>CSharpReference&lt;T&gt;</c>: они сериализуются
    /// строкой выражения и редактируются Roslyn-редактором студии.
    /// </para>
    /// </summary>
    internal sealed class XamlExpressionPolicy
    {
        /// <summary>Подсказка, которая возвращается агенту вместе с нарушением.</summary>
        internal const string Hint =
            "используйте CSharpValue<T>(\"...\") и CSharpReference<T>(\"...\"): лямбда в аргументе " +
            "не сериализуется, и процесс перестаёт открываться в конструкторе";

        private static readonly object Gate = new object();

        private static readonly Dictionary<Type, PropertyInfo[]> ArgumentProperties =
            new Dictionary<Type, PropertyInfo[]>();

        /// <summary>Правило по умолчанию: одно на всю студию.</summary>
        internal static XamlExpressionPolicy Default { get; } = new XamlExpressionPolicy();

        /// <summary>
        /// Проверяет дерево активности: каждый аргумент (<c>InArgument</c>/<c>OutArgument</c>/<c>InOutArgument</c>)
        /// и вложенные активности.
        /// </summary>
        internal XamlExpressionPolicyReport Check(Activity implementation)
        {
            var report = new XamlExpressionPolicyReport();

            if (implementation == null)
            {
                return report;
            }

            foreach (Activity activity in Enumerate(implementation))
            {
                foreach (PropertyInfo property in Properties(activity.GetType()))
                {
                    Argument argument;

                    try
                    {
                        argument = property.GetValue(activity) as Argument;
                    }
                    catch (Exception ex)
                    {
                        // Свойство-аргумент может бросать при чтении незаданного значения: это не наше нарушение.
                        System.Diagnostics.Debug.WriteLine(
                            $"Аргумент «{property.Name}» не прочитан: " + ErrorText.Of(ex));
                        continue;
                    }

                    ActivityWithResult expression = argument?.Expression;

                    if (expression == null || !IsLambda(expression))
                    {
                        continue;
                    }

                    report.AddViolation(
                        $"аргумент «{property.Name}» активности «{ActivityName(activity)}» содержит лямбду " +
                        $"{TypeName(expression.GetType())}: лямбды в аргументах не сериализуются");
                }
            }

            if (!report.Ok)
            {
                report.AddHint(Hint);
            }

            return report;
        }

        /// <summary>
        /// Лямбда ли это: <see cref="LambdaValue{T}"/> или <see cref="LambdaReference{T}"/>
        /// (обе — обобщённые типы из <c>System.Activities.Expressions</c>).
        /// </summary>
        internal static bool IsLambda(ActivityWithResult expression)
        {
            if (expression == null)
            {
                return false;
            }

            Type type = expression.GetType();

            if (!type.IsGenericType)
            {
                return false;
            }

            Type definition = type.GetGenericTypeDefinition();

            return definition == typeof(LambdaValue<>)
                   || definition == typeof(LambdaReference<>);
        }

        /// <summary>Активности дерева: корень всегда, остальные — через <c>WorkflowInspectionServices</c>.</summary>
        private static IEnumerable<Activity> Enumerate(Activity implementation)
        {
            var visited = new HashSet<Activity> { implementation };
            var activities = new List<Activity> { implementation };

            try
            {
                foreach (Activity activity in WorkflowInspectionServices.GetActivities(implementation))
                {
                    if (activity != null && visited.Add(activity))
                    {
                        activities.Add(activity);
                    }
                }
            }
            catch (Exception ex)
            {
                // Невалидное дерево (например Assign без обязательных аргументов) не повод падать:
                // проверяем то, что удалось обойти.
                System.Diagnostics.Debug.WriteLine("Дерево не обойдено политикой выражений: " + ErrorText.Of(ex));
            }

            return activities;
        }

        private static PropertyInfo[] Properties(Type activityType)
        {
            lock (Gate)
            {
                if (ArgumentProperties.TryGetValue(activityType, out PropertyInfo[] cached))
                {
                    return cached;
                }
            }

            PropertyInfo[] properties;

            try
            {
                properties = activityType
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                    .Where(property => typeof(Argument).IsAssignableFrom(property.PropertyType))
                    .ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Аргументы типа «{activityType.Name}» не прочитаны: " + ErrorText.Of(ex));
                properties = Array.Empty<PropertyInfo>();
            }

            lock (Gate)
            {
                ArgumentProperties[activityType] = properties;
            }

            return properties;
        }

        private static string ActivityName(Activity activity)
        {
            return string.IsNullOrWhiteSpace(activity.DisplayName)
                ? TypeName(activity.GetType())
                : activity.DisplayName;
        }

        private static string TypeName(Type type)
        {
            if (type == null)
            {
                return "(неизвестно)";
            }

            if (!type.IsGenericType)
            {
                return type.Name;
            }

            string name = type.Name;
            int arity = name.IndexOf('`');

            return arity > 0 ? name.Substring(0, arity) : name;
        }
    }
}
