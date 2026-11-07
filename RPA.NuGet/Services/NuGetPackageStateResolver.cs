using System;
using NuGet.Versioning;
using RPA.NuGet.Models;

namespace RPA.NuGet.Services
{
    /// <summary>
    /// Чистые правила оформления состояния пакета для панели: какое действие предлагает большая
    /// кнопка, как подписать версию и количество скачиваний. Здесь нет ни WPF, ни сети — поэтому
    /// правила проверяются юнит-тестами (<c>AiRpaStudio.Tests\NuGetPackageStateTests.cs</c>), а
    /// представление и модель только читают результат.
    /// </summary>
    public static class NuGetPackageStateResolver
    {
        /// <summary>
        /// Действие кнопки: у неподключённого пакета — установка, выше текущей версии — обновление,
        /// ниже — понижение, на текущей версии — удаление (как в Visual Studio 2026).
        /// </summary>
        public static NuGetPackageAction ResolveAction(string targetVersion, string installedVersion)
        {
            if (string.IsNullOrWhiteSpace(targetVersion))
            {
                return NuGetPackageAction.None;
            }

            if (string.IsNullOrWhiteSpace(installedVersion))
            {
                return NuGetPackageAction.Install;
            }

            if (NuGetVersion.TryParse(targetVersion, out NuGetVersion target) &&
                NuGetVersion.TryParse(installedVersion, out NuGetVersion installed))
            {
                if (target > installed)
                {
                    return NuGetPackageAction.Update;
                }

                if (target < installed)
                {
                    return NuGetPackageAction.Downgrade;
                }

                return NuGetPackageAction.Uninstall;
            }

            // Версии не разобрались (например, диапазон) — сравниваем строкой: совпали, значит
            // выбранная версия и есть установленная.
            return string.Equals(targetVersion, installedVersion, StringComparison.OrdinalIgnoreCase)
                ? NuGetPackageAction.Uninstall
                : NuGetPackageAction.Update;
        }

        /// <summary>Подпись большой кнопки панели деталей.</summary>
        public static string ActionText(NuGetPackageAction action)
        {
            switch (action)
            {
                case NuGetPackageAction.Install:
                    return "Install";
                case NuGetPackageAction.Update:
                    return "Update";
                case NuGetPackageAction.Downgrade:
                    return "Downgrade";
                case NuGetPackageAction.Uninstall:
                    return "Uninstall";
                default:
                    return "Install";
            }
        }

        /// <summary>Подпись количества скачиваний в стиле VS: <c>8.36B</c>, <c>123.4M</c>, <c>12.3K</c>.</summary>
        public static string FormatDownloads(long downloads)
        {
            if (downloads <= 0)
            {
                return string.Empty;
            }

            if (downloads >= 1_000_000_000L)
            {
                return (downloads / 1_000_000_000d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "B";
            }

            if (downloads >= 1_000_000L)
            {
                return (downloads / 1_000_000d).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "M";
            }

            if (downloads >= 1_000L)
            {
                return (downloads / 1_000d).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "K";
            }

            return downloads.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Подпись версии в выпадающем списке: «Latest stable 10.0.14», «Latest prerelease 11.0.0-beta»,
        /// «9.0.0 (installed)».
        /// </summary>
        public static string VersionLabel(string version, bool isLatestStable, bool isLatestPrerelease, bool isInstalled)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return string.Empty;
            }

            if (isLatestStable)
            {
                return "Latest stable " + version;
            }

            if (isLatestPrerelease)
            {
                return "Latest prerelease " + version;
            }

            return isInstalled ? version + " (installed)" : version;
        }
    }
}
