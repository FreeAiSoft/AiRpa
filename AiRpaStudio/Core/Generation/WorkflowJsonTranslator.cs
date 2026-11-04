using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Результат транспатора «JSON AST → объектная модель WF» (этап 6 плана
    /// <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// </summary>
    internal sealed class WorkflowJsonTranslation
    {
        private WorkflowJsonTranslation()
        {
        }

        internal bool Ok { get; private set; }

        /// <summary>Корень процесса — объектная модель WF, а не текст XAML.</summary>
        internal Activity Activity { get; private set; }

        internal int ActivityCount { get; private set; }

        internal List<string> Errors { get; } = new List<string>();

        internal List<string> Hints { get; } = new List<string>();

        internal static WorkflowJsonTranslation Success(Activity activity, int activityCount, IEnumerable<string> hints = null)
        {
            var result = new WorkflowJsonTranslation
            {
                Ok = true,
                Activity = activity,
                ActivityCount = activityCount
            };

            result.AddHints(hints);
            return result;
        }

        internal static WorkflowJsonTranslation Failure(IEnumerable<string> errors, IEnumerable<string> hints = null)
        {
            var result = new WorkflowJsonTranslation { Ok = false };

            if (errors != null)
            {
                result.Errors.AddRange(errors.Where(error => !string.IsNullOrWhiteSpace(error)));
            }

            result.AddHints(hints);
            return result;
        }

        internal string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine(Ok ? "транспатор json: ок" : "транспатор json: ОШИБКИ");
            text.AppendLine($"активностей: {ActivityCount}");

            foreach (string error in Errors)
            {
                text.AppendLine("  ошибка: " + error);
            }

            foreach (string hint in Hints)
            {
                text.AppendLine("  подсказка: " + hint);
            }

            return text.ToString();
        }

        private void AddHints(IEnumerable<string> hints)
        {
            if (hints == null)
            {
                return;
            }

            foreach (string hint in hints.Where(hint => !string.IsNullOrWhiteSpace(hint)))
            {
                if (!Hints.Contains(hint))
                {
                    Hints.Add(hint);
                }
            }
        }
    }

    /// <summary>
    /// Транспатор JSON AST → <see cref="Activity"/> (этап 6 плана
    /// <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <para>
    /// Своей логики сборки здесь нет и быть не должно: описание превращается в
    /// <see cref="PaletteActivitySpec"/> ещё на этапе валидации, а объектную модель собирает
    /// <see cref="ActivityPaletteFactory"/> — тот же код, что и у палитры. Дальше работает общий
    /// конвейер этапов 2–3 (<see cref="WorkflowBuildService"/> → <see cref="DesignerOpenService"/>),
    /// поэтому JSON AST не может дать «другой» XAML на том же описании.
    /// </para>
    /// </summary>
    internal static class WorkflowJsonTranslator
    {
        /// <summary>Собирает дерево активностей по проверенному описанию.</summary>
        internal static WorkflowJsonTranslation Translate(
            WorkflowJsonDocument document,
            ActivityPaletteFactory factory = null)
        {
            if (document == null || document.Root == null)
            {
                return WorkflowJsonTranslation.Failure(
                    new[] { "xaml.ast: дерево активностей не собрано — нет корня процесса" },
                    new[] { WorkflowJsonSchema.Describe() });
            }

            ActivityPaletteFactory paletteFactory = factory ?? new ActivityPaletteFactory();
            PaletteBuildResult build = paletteFactory.Build(document.Root);

            if (!build.Ok)
            {
                return WorkflowJsonTranslation.Failure(build.Errors, build.Hints);
            }

            return WorkflowJsonTranslation.Success(build.Activity, document.ActivityCount, build.Hints);
        }
    }
}
