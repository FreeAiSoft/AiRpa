using System;
using System.Activities;
using System.Activities.Debugger;
using System.Activities.Debugger.Symbol;
using System.Collections.Generic;
using System.Reflection;

using RPA.Logging;

namespace RPA.Helpers
{
    /// <summary>
    /// Строит рантайм-маппинг <c>Activity → SourceLocation</c> по отладочным символам, вшитым в
    /// скомпилированный XAML (<c>sads:DebugSymbol.Symbol</c> на теле x:Class-активности).
    /// <para>
    /// Дизайнер открыт loose (фасады без тел), а прогон идёт по собранной сборке (реальные тела),
    /// поэтому штатный маппинг верхнего дизайнера не содержит вложенные процессы. Здесь маппинг
    /// строится из самих символов рантайм-дерева и покрывает все скрипты процесса.
    /// </para>
    /// </summary>
    internal static class RuntimeSourceLocationMap
    {
        private static readonly MethodInfo GetSourceLocationsMethod = typeof(SourceLocationProvider).GetMethod(
            "GetSourceLocations",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            new[] { typeof(Activity), typeof(WorkflowSymbol), typeof(bool) },
            null);

        private static readonly PropertyInfo ParentProperty = typeof(Activity).GetProperty(
            "Parent", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly PropertyInfo RootActivityProperty = typeof(Activity).GetProperty(
            "RootActivity", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Рекурсивно обходит <see cref="WorkflowInspectionServices.GetActivities"/> (корень + потомки,
        /// защита от циклов через <see cref="HashSet{T}"/>), для каждой активности с отладочным символом
        /// декодирует его и через reflection вызывает internal
        /// <c>SourceLocationProvider.GetSourceLocations(activity, symbol, false)</c>. Ошибки глушатся
        /// в пустой словарь + файловая диагностика.
        /// </summary>
        internal static Dictionary<object, SourceLocation> Build(Activity rootActivity)
        {
            Dictionary<object, SourceLocation> result = new Dictionary<object, SourceLocation>();

            if (rootActivity == null)
            {
                return result;
            }

            HashSet<Activity> visited = new HashSet<Activity>();
            Queue<Activity> queue = new Queue<Activity>();
            queue.Enqueue(rootActivity);

            while (queue.Count > 0)
            {
                Activity activity = queue.Dequeue();

                if (activity == null || !visited.Add(activity))
                {
                    continue;
                }

                MergeActivitySymbols(activity, result);

                foreach (Activity child in WorkflowInspectionServices.GetActivities(activity))
                {
                    if (child != null && !visited.Contains(child))
                    {
                        queue.Enqueue(child);
                    }
                }
            }

            SourceLocationMappingDiagnostics.Write(
                "RuntimeSourceLocationMap.Build: done total=" + result.Count);

            return result;
        }

        private static void MergeActivitySymbols(Activity activity, Dictionary<object, SourceLocation> result)
        {
            string symbolString = DebugSymbol.GetSymbol(activity) as string;

            if (string.IsNullOrEmpty(symbolString))
            {
                return;
            }

            SourceLocationMappingDiagnostics.Write(
                "RuntimeSourceLocationMap: symbol found on " + SourceLocationMappingDiagnostics.Describe(activity));

            try
            {
                WorkflowSymbol symbol = WorkflowSymbol.Decode(symbolString);

                if (symbol == null)
                {
                    SourceLocationMappingDiagnostics.Write("RuntimeSourceLocationMap: decoded symbol is null");
                    return;
                }

                if (GetSourceLocationsMethod == null)
                {
                    SourceLocationMappingDiagnostics.Write(
                        "RuntimeSourceLocationMap: internal SourceLocationProvider.GetSourceLocations(Activity, WorkflowSymbol, bool) not found");
                    return;
                }

                // В скомпилированном XAML символ лежит на теле x:Class-активности (первый ребёнок),
                // поэтому корнем для GetSourceLocations обязана быть сама x:Class-активность — parent тела.
                // См. reference source: if (rootActivity.RootActivity != rootActivity) rootActivity = rootActivity.Parent.
                Activity symbolRoot = activity;
                Activity root = RootActivityProperty?.GetValue(activity) as Activity;

                if (root != null && root != activity)
                {
                    Activity parent = ParentProperty?.GetValue(activity) as Activity;

                    if (parent != null)
                    {
                        symbolRoot = parent;
                    }
                }

                Dictionary<object, SourceLocation> mapping =
                    GetSourceLocationsMethod.Invoke(null, new object[] { symbolRoot, symbol, false })
                    as Dictionary<object, SourceLocation>;

                if (mapping == null)
                {
                    SourceLocationMappingDiagnostics.Write(
                        "RuntimeSourceLocationMap: reflection returned null for " + SourceLocationMappingDiagnostics.Describe(symbolRoot));
                    return;
                }

                foreach (KeyValuePair<object, SourceLocation> entry in mapping)
                {
                    result[entry.Key] = entry.Value;
                    SourceLocationMappingDiagnostics.Write(
                        "RuntimeSourceLocationMap: entry key=" + DescribeKey(entry.Key)
                        + " -> " + SourceLocationMappingDiagnostics.Describe(entry.Value));
                }

                SourceLocationMappingDiagnostics.Write(
                    "RuntimeSourceLocationMap: merged " + mapping.Count + " entries (total " + result.Count + ")");
            }
            catch (Exception ex)
            {
                SourceLocationMappingDiagnostics.WriteException(
                    ex,
                    "RuntimeSourceLocationMap: failed on " + SourceLocationMappingDiagnostics.Describe(activity));
            }
        }
        private static string DescribeKey(object key)
        {
            if (key is Activity activity)
            {
                return SourceLocationMappingDiagnostics.Describe(activity);
            }

            return key?.GetType().FullName ?? "<null>";
        }

    }
}
