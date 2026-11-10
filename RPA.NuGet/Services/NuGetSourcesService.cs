using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
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
        /// <summary>Единственное место, где в коде хранится URL NuGet API — seed первичной настройки.</summary>
        private const string DefaultNuGetSourceUrl = "https://api.nuget.org/v3/index.json";

        private const string DefaultNuGetSourceName = "nuget.org";

        public const string SourceMappingSection = "packageSourceMapping";

        /// <summary>
        /// Источники, доступные проекту в <paramref name="projectDirectory"/>. Если рядом с проектом
        /// (или на уровне пользователя) нет <c>NuGet.Config</c> с источниками — создаётся отключённый
        /// источник nuget.org (первичная настройка источников).
        /// </summary>
        public IReadOnlyList<NuGetSourceDefinition> Load(string projectDirectory)
        {
            // Первичная настройка: если конфигурации нет или в ней нет источников, создаём
            // отключённый источник nuget.org. URL NuGet API живёт только здесь и только как seed.
            string configPath = ResolveSeedConfigPath(projectDirectory);
            if (!ConfigHasPackageSources(configPath))
                EnsureDefaultSource(configPath);

            ISettings settings = TryLoadSettings(projectDirectory);
            if (settings == null)
            {
                NuGetLog.Warn("NuGet configuration could not be loaded; returning empty source list.");
                return new List<NuGetSourceDefinition>();
            }

            List<NuGetSourceDefinition> result = ReadPackageSources(settings);

            NuGetLog.Info("NuGet sources loaded: " + result.Count + " source(s) from " + (string.IsNullOrWhiteSpace(projectDirectory) ? "<current dir>" : projectDirectory));
            if (result.Count == 0)
                NuGetLog.Warn("No package sources available; all sources are disabled or config is empty.");

            return result
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
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
            NuGetLog.Info("NuGet sources saved: " + packageSources.Count + " source(s)");
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

        private static List<NuGetSourceDefinition> ReadPackageSources(ISettings settings)
        {
            var result = new List<NuGetSourceDefinition>();

            foreach (PackageSource source in LoadPackageSources(settings))
            {
                if (source == null || string.IsNullOrWhiteSpace(source.Source))
                    continue;

                result.Add(new NuGetSourceDefinition
                {
                    Name = string.IsNullOrWhiteSpace(source.Name) ? source.Source : source.Name,
                    Url = source.Source,
                    IsEnabled = source.IsEnabled
                });
            }

            return result;
        }

        /// <summary>
        /// Есть ли в конкретном <c>NuGet.Config</c> явно объявленные источники. Проверяем файл,
        /// а не объединённую цепочку: так первичная настройка срабатывает и при наличии
        /// пользовательского/глобального конфига.
        /// </summary>
        private static bool ConfigHasPackageSources(string configPath)
        {
            try
            {
                if (!File.Exists(configPath))
                    return false;

                XDocument document = XDocument.Load(configPath);
                XElement packageSources = document.Root?.Element("packageSources");
                return packageSources != null && packageSources.Elements("add").Any();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Первичная настройка источников пакетов: записывает источник nuget.org
        /// (URL api.nuget.org) отключённым.
        /// </summary>
        private void EnsureDefaultSource(string configPath)
        {
            try
            {
                NuGetLog.Info("Seeding disabled nuget.org source into " + configPath);
                WriteSeedConfig(configPath);
            }
            catch (Exception ex)
            {
                NuGetLog.Error(ex, "Failed to seed disabled nuget.org source");
            }
        }

        private static string ResolveSeedConfigPath(string projectDirectory)
        {
            if (!string.IsNullOrWhiteSpace(projectDirectory) && Directory.Exists(projectDirectory))
                return Path.Combine(projectDirectory, "NuGet.Config");

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NuGet",
                "NuGet.Config");
        }

        private void WriteSeedConfig(string configPath)
        {
            XDocument document = File.Exists(configPath) ? XDocument.Load(configPath) : new XDocument(new XElement("configuration"));
            XElement root = document.Root;
            if (root == null)
            {
                root = new XElement("configuration");
                document.Add(root);
            }

            XElement packageSources = root.Element("packageSources");
            if (packageSources == null)
            {
                packageSources = new XElement("packageSources");
                root.AddFirst(packageSources);
            }

            if (packageSources.Elements("add").Any(e =>
                    string.Equals((string)e.Attribute("key"), DefaultNuGetSourceName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            packageSources.Add(new XElement("add",
                new XAttribute("key", DefaultNuGetSourceName),
                new XAttribute("value", DefaultNuGetSourceUrl)));

            XElement disabled = root.Element("disabledPackageSources");
            if (disabled == null)
            {
                disabled = new XElement("disabledPackageSources");
                root.Add(disabled);
            }

            disabled.Add(new XElement("add",
                new XAttribute("key", DefaultNuGetSourceName),
                new XAttribute("value", "true")));

            string directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            document.Save(configPath);
        }

        private static IEnumerable<PackageSource> LoadPackageSources(ISettings settings)
        {
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
