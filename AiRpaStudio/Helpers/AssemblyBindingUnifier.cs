using System;
using System.Collections.Generic;
using System.Reflection;
using RPA.Logging;

namespace RPA.Helpers
{
    /// <summary>
    /// Страховка от несогласованных версий зависимостей в папке приложения.
    /// <para>
    /// Инцидент: при загрузке проекта WF-панель «Пространства имён» (<c>ImportDesigner</c>) перечисляет
    /// типы сборок через <c>Assembly.GetExportedTypes()</c>. Студия перенаправляет
    /// <c>System.Threading.Tasks.Extensions</c> на 4.2.0.1 (пакет 4.5.4), но если рядом лежит копия
    /// 4.2.0.0 (пакет 4.5.2) — привязка падает с <c>FileLoadException</c> (HRESULT 0x80131040), и WF
    /// показывает модальное окно «Конструктор рабочих процессов», блокирующее работу.
    /// </para>
    /// <para>
    /// Обработчик <see cref="AppDomain.AssemblyResolve"/> вызывается уже после неудачной привязки и
    /// возвращает сборку с тем же простым именем, тем же открытым ключом и <b>не меньшей</b> версией.
    /// Подменяются только «стронгнейм»-запросы конкретной версии: сателлиты ресурсов (другая культура),
    /// запросы без версии и понижение версии не трогаются — там подмена меняет смысл загрузки.
    /// Обработчик никогда не бросает исключений: если подходящей сборки нет, возвращается <c>null</c>
    /// и привязка завершается штатной ошибкой.
    /// </para>
    /// </summary>
    internal static class AssemblyBindingUnifier
    {
        private const string ResourcesSuffix = ".resources";

        private static readonly object SyncRoot = new object();
        private static int _subscribeCount;

        /// <summary>Обработчик установлен в текущем домене.</summary>
        internal static bool IsInstalled => _subscribeCount > 0;

        /// <summary>Количество фактических подписок (для проверки идемпотентности).</summary>
        internal static int SubscribeCount => _subscribeCount;

        /// <summary>
        /// Ставит обработчик <see cref="AppDomain.AssemblyResolve"/>. Повторный вызов ничего не делает.
        /// </summary>
        internal static void Install(AppDomain domain)
        {
            if (domain == null)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (_subscribeCount > 0)
                {
                    return;
                }

                domain.AssemblyResolve += ResolveAssembly;
                _subscribeCount++;
            }
        }

        /// <summary>
        /// Обработчик <see cref="AppDomain.AssemblyResolve"/>: подменяет неудавшуюся привязку уже
        /// загруженной сборкой того же имени и ключа. Не бросает исключений.
        /// </summary>
        internal static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            try
            {
                string requestedName = args?.Name;
                Assembly match = FindLoadedMatch(requestedName, AppDomain.CurrentDomain.GetAssemblies());
                if (match != null)
                {
                    Log.Write($"Привязка сборки: запрос «{requestedName}» заменён уже загруженной «{match.FullName}». "
                              + "Проверьте версии пакетов в папке приложения (несогласованные копии одной сборки).");
                }

                return match;
            }
            catch (Exception ex)
            {
                Log.Write("Привязка сборки: обработчик не смог подобрать замену.", ex);
                return null;
            }
        }

        /// <summary>
        /// Ищет среди загруженных сборку с тем же простым именем и открытым ключом, версия которой
        /// не меньше запрошенной (точное совпадение — приоритет). Возвращает <c>null</c>, если запрос
        /// не про конфликт версий: сателлит ресурсов, запрос без версии/ключа, только понижение версии.
        /// </summary>
        internal static Assembly FindLoadedMatch(string requestedFullName, IEnumerable<Assembly> loadedAssemblies)
        {
            if (string.IsNullOrWhiteSpace(requestedFullName) || loadedAssemblies == null)
            {
                return null;
            }

            AssemblyName requested;
            try
            {
                requested = new AssemblyName(requestedFullName);
            }
            catch (Exception)
            {
                // «Не имя сборки» — не наша задача, штатная ошибка привязки.
                return null;
            }

            if (string.IsNullOrEmpty(requested.Name)
                || requested.Version == null
                || requested.Name.EndsWith(ResourcesSuffix, StringComparison.OrdinalIgnoreCase)
                || IsCultureSpecific(requested.CultureName))
            {
                return null;
            }

            byte[] requestedToken = requested.GetPublicKeyToken();
            if (requestedToken == null || requestedToken.Length == 0)
            {
                // Без открытого ключа сверить нечего: подмена может подсунуть чужую сборку.
                return null;
            }

            Assembly best = null;
            Version bestVersion = null;

            foreach (Assembly candidate in loadedAssemblies)
            {
                if (candidate == null || candidate.IsDynamic)
                {
                    continue;
                }

                AssemblyName candidateName;
                try
                {
                    candidateName = candidate.GetName();
                }
                catch (Exception)
                {
                    continue;
                }

                if (!string.Equals(candidateName.Name, requested.Name, StringComparison.OrdinalIgnoreCase)
                    || candidateName.Version == null
                    || candidateName.Version < requested.Version
                    || !PublicKeyTokenMatches(requestedToken, candidateName))
                {
                    continue;
                }

                if (candidateName.Version == requested.Version)
                {
                    return candidate;
                }

                if (bestVersion == null || candidateName.Version > bestVersion)
                {
                    best = candidate;
                    bestVersion = candidateName.Version;
                }
            }

            return best;
        }

        private static bool IsCultureSpecific(string cultureName)
        {
            return !string.IsNullOrEmpty(cultureName)
                   && !string.Equals(cultureName, "neutral", StringComparison.OrdinalIgnoreCase);
        }

        private static bool PublicKeyTokenMatches(byte[] requestedToken, AssemblyName candidate)
        {
            byte[] candidateToken = candidate.GetPublicKeyToken();
            if (candidateToken == null || candidateToken.Length != requestedToken.Length)
            {
                return false;
            }

            for (int i = 0; i < requestedToken.Length; i++)
            {
                if (requestedToken[i] != candidateToken[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
