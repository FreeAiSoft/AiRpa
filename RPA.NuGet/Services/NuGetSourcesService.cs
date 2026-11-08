using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NuGet.Configuration;
using RPA.NuGet.Models;

namespace RPA.NuGet.Services
{
    /// <summary>
    /// Источники пакетов для выпадающего списка «Package source». Читает обычную цепочку
    /// <c>NuGet.Config</c> (solution → user → global, как это делает NuGet restore) и сообщает,
    /// настроено ли сопоставление источников (package source mapping) — панель показывает это
    /// предупреждением, как Visual Studio.
    /// </summary>
    public class NuGetSourcesService
    {
        public const string NuGetOrgName = "nuget.org";
        public const string NuGetOrgUrl = "https://api.nuget.org/v3/index.json";
        public const string SourceMappingSection = "packageSourceMapping";

        /// <summary>
        /// Источники, доступные проекту в <paramref name="projectDirectory"/>. Если конфигурации нет
        /// или она пуста, возвращается единственный nuget.org с <see cref="NuGetSourceDefinition.IsFallback"/>.
        /// </summary>
        public IReadOnlyList<NuGetSourceDefinition> Load(string projectDirectory)
        {
            var result = new List<NuGetSourceDefinition>();

            foreach (PackageSource source in LoadPackageSources(projectDirectory))
            {
                if (source == null || string.IsNullOrWhiteSpace(source.Source))
                    continue;

                // Показываем все источники, включая локальные папки: оверлей «Package sources»
                // обязан повторять NuGet.Config целиком (как в Visual Studio).
                result.Add(new NuGetSourceDefinition
                {
                    Name = string.IsNullOrWhiteSpace(source.Name) ? source.Source : source.Name,
                    Url = source.Source,
                    IsEnabled = source.IsEnabled
                });
            }

            NuGetLog.Info("NuGet sources loaded: " + result.Count + " source(s) from " + (string.IsNullOrWhiteSpace(projectDirectory) ? "<current dir>" : projectDirectory));
            if (result.Count == 0)
            {
                result.Add(new NuGetSourceDefinition
                {
                    Name = NuGetOrgName,
                    Url = NuGetOrgUrl,
                    IsFallback = true
                });
            }

            // nuget.org первым: он же источник по умолчанию у Visual Studio и у прежней версии панели.
            return result
                .OrderByDescending(s => string.Equals(s.Url, NuGetOrgUrl, StringComparison.OrdinalIgnoreCase))
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Записывает список источников в ближайший <c>NuGet.Config</c> (уровня solution/user):
        /// имя, адрес и состояние «Enabled» (NuGet хранит выключенные источники отдельной секцией).
        /// </summary>
        public void Save(string projectDirectory, IEnumerable<NuGetSourceDefinition> sources)
        {
            if (sources == null)
                throw new ArgumentNullException(nameof(sources));

            ISettings settings = TryLoadSettings(projectDirectory);
            if (settings == null)
                throw new InvalidOperationException("NuGet configuration could not be loaded.");

            var packageSources = new List<PackageSource>();
            foreach (NuGetSourceDefinition source in sources)
            {
                if (source == null || string.IsNullOrWhiteSpace(source.Url))
                    continue;

                string name = string.IsNullOrWhiteSpace(source.Name) ? source.Url : source.Name.Trim();
                packageSources.Add(new PackageSource(source.Url.Trim(), name, source.IsEnabled));
            }

            var provider = new PackageSourceProvider(settings);
            provider.SavePackageSources(packageSources);
        }

        /// <summary>Настроено ли сопоставление источников (<c>&lt;packageSourceMapping&gt;</c> в NuGet.Config).</summary>
        public bool IsSourceMappingConfigured(string projectDirectory)
        {
            ISettings settings = TryLoadSettings(projectDirectory);
            if (settings == null)
                return false;

            try
            {
                return settings.GetSection(SourceMappingSection) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Путь к ближайшему <c>NuGet.Config</c>, который открывает ссылка «Configure».</summary>
        public string FindConfigPath(string projectDirectory)
        {
            ISettings settings = TryLoadSettings(projectDirectory);
            if (settings == null)
                return null;

            try
            {
                IList<string> paths = settings.GetConfigFilePaths();
                return paths?.FirstOrDefault(File.Exists);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static IEnumerable<PackageSource> LoadPackageSources(string projectDirectory)
        {
            ISettings settings = TryLoadSettings(projectDirectory);
            if (settings == null)
                return Enumerable.Empty<PackageSource>();

            try
            {
                return new PackageSourceProvider(settings).LoadPackageSources() ?? Enumerable.Empty<PackageSource>();
            }
            catch (Exception)
            {
                // Битый/нечитаемый NuGet.Config не должен ломать панель.
                return Enumerable.Empty<PackageSource>();
            }
        }

        private static ISettings TryLoadSettings(string projectDirectory)
        {
            try
            {
                string root = string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory)
                    ? Directory.GetCurrentDirectory()
                    : projectDirectory;

                return Settings.LoadDefaultSettings(root);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
