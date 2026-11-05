using System;
using System.Collections.Generic;
using System.Text;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Текстовые ответы инструментов <c>activity_search</c> и <c>activity_describe</c> (карточка
    /// <c>AG-01</c>).
    /// <para>
    /// Формат держится отдельно от каталога и от регистрации инструментов: его проверяют тесты,
    /// а каталог можно менять, не переписывая ожидаемый текст.
    /// </para>
    /// </summary>
    public static class ActivityCatalogText
    {
        /// <summary>Полное описание активности: аргументы с типами и направлениями, контейнеры.</summary>
        public static string DescribeActivity(ActivityInfo activity)
        {
            if (activity == null)
            {
                return string.Empty;
            }

            var text = new StringBuilder();

            text.AppendLine($"{activity.Key} — {activity.TypeName}");
            text.AppendLine("отображаемое имя: " + activity.DisplayName);
            text.AppendLine("категория: " + (string.IsNullOrWhiteSpace(activity.Category) ? "без категории" : activity.Category));

            if (activity.IsGeneric)
            {
                text.AppendLine($"обобщённая: да, аргументов типа — {activity.GenericArity}");
            }

            if (!string.IsNullOrWhiteSpace(activity.Description))
            {
                text.AppendLine("описание: " + activity.Description.Trim());
            }

            if (activity.Arguments.Count == 0)
            {
                text.AppendLine("аргументы: нет");
            }
            else
            {
                text.AppendLine("аргументы:");
                foreach (ActivityArgumentInfo argument in activity.Arguments)
                {
                    var line = new StringBuilder("  - ").Append(argument.Name)
                        .Append(" (").Append(argument.DirectionName).Append(", ").Append(argument.TypeName);

                    if (argument.Required)
                    {
                        line.Append(", обязателен");
                    }

                    if (!string.IsNullOrWhiteSpace(argument.DefaultValue))
                    {
                        line.Append(", по умолчанию ").Append(argument.DefaultValue);
                    }

                    line.Append(')');

                    if (!string.IsNullOrWhiteSpace(argument.Description))
                    {
                        line.Append(" — ").Append(argument.Description.Trim());
                    }

                    text.AppendLine(line.ToString());
                }
            }

            if (activity.Containers.Count == 0)
            {
                text.AppendLine("дочерние активности не принимает");
            }
            else
            {
                text.AppendLine("контейнеры (куда вкладывать дочерние активности):");
                foreach (ActivityContainerInfo container in activity.Containers)
                {
                    var line = new StringBuilder("  - ").Append(container.Name)
                        .Append(" (").Append(container.Kind).Append(", ").Append(container.ChildTypeName).Append(')');

                    if (!string.IsNullOrWhiteSpace(container.Description))
                    {
                        line.Append(" — ").Append(container.Description.Trim());
                    }

                    text.AppendLine(line.ToString());
                }
            }

            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// Список результатов поиска: по строке на активность, с указанием, сколько всего нашлось
        /// и сколько показано (предел <c>limit</c>).
        /// </summary>
        public static string DescribeSearch(
            string query,
            string category,
            IReadOnlyList<ActivityInfo> found,
            int totalInCatalog,
            int limit)
        {
            var criteria = new List<string>();
            if (!string.IsNullOrWhiteSpace(query))
            {
                criteria.Add("запрос «" + query.Trim() + "»");
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                criteria.Add("категория «" + category.Trim() + "»");
            }

            string header = criteria.Count == 0
                ? "Каталог активностей"
                : "Поиск: " + string.Join(", ", criteria);

            var text = new StringBuilder();
            text.AppendLine($"{header} — найдено {found?.Count ?? 0} из {totalInCatalog}.");

            if (found == null || found.Count == 0)
            {
                text.AppendLine("Ничего не найдено. Уточните запрос или вызовите activity_search без query, чтобы увидеть весь каталог.");
                return text.ToString().TrimEnd();
            }

            foreach (ActivityInfo activity in found)
            {
                text.Append("- ").Append(activity.Key)
                    .Append(" — ").Append(activity.TypeName)
                    .Append(" [").Append(string.IsNullOrWhiteSpace(activity.Category) ? "без категории" : activity.Category).Append(']');

                text.Append("; аргументы: ");
                text.Append(activity.Arguments.Count == 0 ? "нет" : JoinArguments(activity.Arguments));

                text.Append("; контейнеры: ");
                text.Append(activity.Containers.Count == 0 ? "нет" : JoinContainers(activity.Containers));

                text.AppendLine();
            }

            if (limit > 0 && found.Count >= limit)
            {
                text.AppendLine($"Показаны первые {limit}; увеличьте limit или уточните запрос. Полное описание — activity_describe(typeName).");
            }
            else
            {
                text.AppendLine("Полное описание активности — activity_describe(typeName).");
            }

            return text.ToString().TrimEnd();
        }

        /// <summary>Ответ на неизвестное имя: что искали и что похоже по каталогу.</summary>
        public static string DescribeUnknown(string typeName, IReadOnlyList<ActivityInfo> suggestions)
        {
            var text = new StringBuilder();
            text.Append("Активность не найдена: «").Append((typeName ?? string.Empty).Trim()).Append("».");

            if (suggestions != null && suggestions.Count > 0)
            {
                text.Append(" Похожие: ");
                for (int index = 0; index < suggestions.Count; index++)
                {
                    if (index > 0)
                    {
                        text.Append(", ");
                    }

                    text.Append(suggestions[index].Key);
                }

                text.Append('.');
            }

            text.Append(" Вызовите activity_search(query) или activity_search без query, чтобы получить список каталога.");
            return text.ToString();
        }

        private static string JoinArguments(IReadOnlyList<ActivityArgumentInfo> arguments)
        {
            var text = new StringBuilder();
            for (int index = 0; index < arguments.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(", ");
                }

                ActivityArgumentInfo argument = arguments[index];
                text.Append(argument.Name).Append('(').Append(argument.DirectionName).Append(", ").Append(argument.TypeName);
                if (argument.Required)
                {
                    text.Append(", обязателен");
                }

                text.Append(')');
            }

            return text.ToString();
        }

        private static string JoinContainers(IReadOnlyList<ActivityContainerInfo> containers)
        {
            var text = new StringBuilder();
            for (int index = 0; index < containers.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(", ");
                }

                ActivityContainerInfo container = containers[index];
                text.Append(container.Name).Append('(').Append(container.Kind).Append(", ").Append(container.ChildTypeName).Append(')');
            }

            return text.ToString();
        }
    }
}
