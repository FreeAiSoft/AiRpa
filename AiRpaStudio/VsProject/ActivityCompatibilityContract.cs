using System;
using System.Collections.Generic;

namespace RPA.VsProject
{
    /// <summary>
    /// Единый источник истины для контракта совместимости библиотек активностей со студией
    /// (<c>AiRpaStudio/Docs/ru/activity-development.md</c>).
    /// <para>
    /// Студия работает на .NET Framework 4.8, а роботы и библиотеки активностей — на 4.7.2.
    /// Студия и робот делят один AppDomain, поэтому пересекающиеся со студией зависимости
    /// активности обязаны иметь ту же версию, что и хост. Эталонные («пин») версии собраны здесь,
    /// чтобы и проверка в UI (<see cref="ActivityCompatibilityChecker"/>), и тесты-стражи
    /// (<c>ActivityDependencyContractTests</c>) сверялись с одним и тем же списком.
    /// </para>
    /// </summary>
    public static class ActivityCompatibilityContract
    {
        /// <summary>Целевой TFM библиотеки активностей.</summary>
        public const string RequiredTfm = "net472";

        /// <summary>
        /// Дизайнерская сборка, ссылку на которую активностям иметь запрещено: её атрибуты
        /// (<c>[ToolboxBrowsable]</c>, <c>[ToolboxItem]</c>) читаются при ленивой материализации
        /// тулбокса WF и приводят к пустой категории палитры (<c>FileNotFoundException</c>).
        /// </summary>
        public const string ForbiddenDesignerAssembly = "Microsoft.Windows.Design.Interaction";

        /// <summary>
        /// Устаревшая сборка активностей: новые активности должны ссылаться на
        /// <c>ActivityLibraryBase</c>, а не на старый граф <c>Activities</c>.
        /// </summary>
        public const string LegacyActivitiesAssembly = "Activities";

        /// <summary>
        /// Пин-версии — версии тех же пакетов, что закреплены в <c>AiRpaStudio\packages.config</c>.
        /// Пакеты, которых нет в этом словаре (например, <c>PuppeteerSharp</c>), считаются
        /// уникальными зависимостями активности и не проверяются.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> PinnedVersions =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["System.Text.Json"] = "8.0.6",
                ["System.Runtime.CompilerServices.Unsafe"] = "6.1.2",
                ["System.Memory"] = "4.6.3",
                ["System.Diagnostics.DiagnosticSource"] = "8.0.1",
                ["System.Buffers"] = "4.6.1",
                ["Microsoft.Extensions.DependencyInjection.Abstractions"] = "8.0.2",
                ["Microsoft.Extensions.DependencyInjection"] = "8.0.0",
                ["Microsoft.Extensions.Logging"] = "8.0.0",
                ["Microsoft.Extensions.Logging.Abstractions"] = "8.0.0",
                ["Microsoft.Extensions.Options"] = "8.0.0",
                ["Microsoft.Extensions.Primitives"] = "8.0.0",
                ["System.Numerics.Vectors"] = "4.6.1",
                ["System.Threading.Tasks.Extensions"] = "4.5.4",
                ["System.ValueTuple"] = "4.5.0",
                ["Newtonsoft.Json"] = "13.0.2",
            };

        /// <summary>
        /// Возвращает пин-версию для пакета, пересекающегося со студией, либо <c>null</c>,
        /// если пакет — уникальная зависимость активности (его версия не проверяется).
        /// </summary>
        public static string TryGetPinnedVersion(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return null;
            }

            return PinnedVersions.TryGetValue(packageId.Trim(), out string version) ? version : null;
        }

        /// <summary>Пакет пересекается со студией и обязан иметь пин-версию.</summary>
        public static bool IsPinned(string packageId)
        {
            return TryGetPinnedVersion(packageId) != null;
        }
    }
}
