using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Ответ инструмента <c>activity_create</c> (карточка <c>AG-08</c>): что создано, что изменено,
    /// выполнялась ли сборка, обновилась ли палитра и что осталось на решение человека.
    /// <para>
    /// Три состояния названы явно, как и в остальных инструментах волны: «сборка не запрашивалась»,
    /// «сборка прошла, но файл не найден» и «палитра не обновлена» — это разные ответы, и ни один из них
    /// нельзя показывать как успех.
    /// </para>
    /// </summary>
    internal static class ActivityCreateText
    {
        internal static string Describe(ActivityCreateResult result, ActivityPaletteRefreshResult palette)
        {
            if (result == null)
            {
                return "activity_create: результат не получен";
            }

            var text = new StringBuilder();

            if (!result.Succeeded)
            {
                text.Append("activity_create: ошибка — ").Append(result.Message);

                if (result.BuildErrors.Count > 0)
                {
                    text.AppendLine().AppendLine("ошибки сборки:");
                    foreach (string error in result.BuildErrors)
                    {
                        text.Append("- ").AppendLine(error);
                    }
                }

                return text.ToString().TrimEnd();
            }

            text.Append("activity_create: активность ").Append(result.FullTypeName).AppendLine(" создана.");

            AppendList(text, "создано", result.CreatedFiles, fileNameOnly: true);
            AppendList(text, "изменено", result.ModifiedFiles, fileNameOnly: true);

            text.Append("сборка: ");
            if (!result.BuildPerformed)
            {
                text.AppendLine("не запрашивалась (build=false)");
            }
            else if (result.AssemblyPath == null)
            {
                text.Append("выполнена (").Append(result.Configuration).AppendLine("), ошибок нет, но файл сборки не найден");
            }
            else
            {
                text.Append("выполнена (").Append(result.Configuration).Append("), ошибок нет; файл: ")
                    .AppendLine(result.AssemblyPath);
            }

            if (palette != null)
            {
                text.Append("палитра: ");
                text.AppendLine(palette.Refreshed
                    ? "обновлена, добавлено типов: " + palette.ActivityTypeCount + " (сборка " + palette.AssemblyName + ")"
                    : "не обновлена — " + palette.Reason);
            }

            AppendList(text, "требует решения", result.RequiresDecision, fileNameOnly: false);

            return text.ToString().TrimEnd();
        }

        private static void AppendList(StringBuilder text, string title, IReadOnlyList<string> items, bool fileNameOnly)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            text.AppendLine(title + ":");
            foreach (string item in items)
            {
                text.Append("- ").AppendLine(fileNameOnly ? Path.GetFileName(item) : item);
            }
        }
    }
}
