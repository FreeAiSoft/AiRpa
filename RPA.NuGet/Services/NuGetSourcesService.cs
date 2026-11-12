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
    /// Чтение источников пакетов NuGet с машины/решения и запись по явному запросу (экспорт/создание
    /// конфигурации). Per-project список источников студии здесь не хранится и не пишется: он живёт в
    /// собственном хранилище AiRpaStudio, а панель подключает его хуками.
    /// <para>
    /// Сервис НЕ создаёт и не изменяет <c>NuGet.Config</c> автоматически — только читает существующие
    /// конфиги, а запись возможна исключительно через явные методы (<see cref="Save"/>).
    /// </para>
    /// </summary>
    public class NuGetSourcesService
    {
        public const string SourceMappingSection = "packageSourceMapping";

        /// <summary>
        /// Объединённый список источников из цепочки <c>NuGet.Config</c> (solution → user → machine),
        /// как это делает NuGet restore. Ничего не пишет и не создаёт файлы.
        /// </summary>
        public IReadOnlyList<NuGetSourceDefinition> Load(string projectDirectory)
        {
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
        /// Источники уровня машины и пользователя (как «Package Sources» в Visual Studio). Только чтение;
        /// источники помечаются <see cref="NuGetSourceOrigin.Machine"/>/<see cref="NuGetSourceOrigin.User"/>
        /// и <see cref="NuGetSourceDefinition.IsReadOnly"/> = true.
        /// </summary>
        public IReadOnlyList<NuGetSourceDefinition> LoadMachineAndUserSources()
        {
            var files = new List<ConfigFile>();
            foreach (string directory in MachineConfigDirectories())
            {
                if (!Directory.Exists(directory))
                    continue;

                foreach (string path in Directory.EnumerateFiles(directory, "*.config", SearchOption.TopDirectoryOnly))
                    files.Add(new ConfigFile { Path = path, Origin = NuGetSourceOrigin.Machine });
            }

            string userConfig = UserConfigPath();
            if (File.Exists(userConfig))
                files.Add(new ConfigFile { Path = userConfig, Origin = NuGetSourceOrigin.User });

            return MergeConfigFiles(files);
        }

        /// <summary>
        /// Источники уровня решения: <c>NuGet.Config</c>/<c>nuget.config</c> в каждой папке от корня
        /// диска до каталога проекта. Только чтение (<see cref="NuGetSourceOrigin.Solution"/>).
        /// </summary>
        public IReadOnlyList<NuGetSourceDefinition> LoadSolutionSources(string projectDirectory)
        {
            var files = new List<ConfigFile>();
            string current = string.IsNullOrWhiteSpace(projectDirectory)
                ? Directory.GetCurrentDirectory()
                : projectDirectory;

            // Идём от корня вниз к проекту — порядок от низшего приоритета к высшему.
            var chain = new Stack<string>();
            for (DirectoryInfo directory = new DirectoryInfo(current); directory != null; directory = directory.Parent)
                chain.Push(directory.FullName);

            while (chain.Count > 0)
            {
                string folder = chain.Pop();
                string nugetConfig = Path.Combine(folder, "NuGet.Config");
                string lowerConfig = Path.Combine(folder, "nuget.config");

                if (File.Exists(nugetConfig))
                    files.Add(new ConfigFile { Path = nugetConfig, Origin = NuGetSourceOrigin.Solution });
                else if (File.Exists(lowerConfig))
                    files.Add(new ConfigFile { Path = lowerConfig, Origin = NuGetSourceOrigin.Solution });
            }

            return MergeConfigFiles(files);
        }

        /// <summary>
        /// Записывает список источников в ближайший <c>NuGet.Config</c> (уровня solution/user).
        /// Используется только для явного действия пользователя «Export to NuGet.Config».
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

        /// <summary>
        /// Явно записывает источники в <c>NuGet.Config</c> по указанному пути (создаёт/перезаписывает файл).
        /// Используется только по команде пользователя («Create/Export NuGet.Config»).
        /// </summary>
        public void WriteConfig(string configPath, IEnumerable<NuGetSourceDefinition> sources)
        {
            if (string.IsNullOrWhiteSpace(configPath))
                throw new ArgumentNullException(nameof(configPath));
            if (sources == null)
                throw new ArgumentNullException(nameof(sources));

            var document = new XDocument(new XElement("configuration"));
            XElement root = document.Root;

            var packageSources = new XElement("packageSources");
            var disabled = new XElement("disabledPackageSources");

            foreach (NuGetSourceDefinition source in sources)
            {
                if (source == null || string.IsNullOrWhiteSpace(source.Url))
                    continue;

                string name = string.IsNullOrWhiteSpace(source.Name) ? source.Url : source.Name.Trim();
                packageSources.Add(new XElement("add",
                    new XAttribute("key", name),
                    new XAttribute("value", source.Url.Trim())));

                if (!source.IsEnabled)
                {
                    disabled.Add(new XElement("add",
                        new XAttribute("key", name),
                        new XAttribute("value", "true")));
                }
            }

            root.Add(packageSources);
            if (disabled.Elements("add").Any())
                root.Add(disabled);

            string directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            document.Save(configPath);
            NuGetLog.Info("NuGet.Config written: " + configPath);
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
                    IsEnabled = source.IsEnabled,
                    Origin = NuGetSourceOrigin.Solution,
                });
            }

            return result;
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

        private static IEnumerable<string> MachineConfigDirectories()
        {
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "NuGet", "Config");
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NuGet", "Config");
        }

        private static string UserConfigPath()
        {
            // NuGet позволяет переопределить пользовательский конфиг переменной окружения
            // (dotnet restore / msbuild -t:restore читают её точно так же).
            string overridePath = Environment.GetEnvironmentVariable("NUGET_CONFIG_FILE");
            if (!string.IsNullOrWhiteSpace(overridePath))
                return overridePath;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NuGet",
                "NuGet.Config");
        }

        /// <summary>
        /// Сливает файлы в порядке приоритета (сначала машина, затем пользователь, затем решение) и
        /// помечает происхождение. По ключу источника «ближайший» файл побеждает, как в NuGet.
        /// </summary>
        private static List<NuGetSourceDefinition> MergeConfigFiles(IEnumerable<ConfigFile> files)
        {
            var merged = new Dictionary<string, NuGetSourceDefinition>(StringComparer.OrdinalIgnoreCase);

            foreach (ConfigFile file in files)
            {
                if (!File.Exists(file.Path))
                    continue;

                ConfigReadResult parsed;
                try
                {
                    parsed = ReadConfig(file.Path);
                }
                catch (Exception)
                {
                    continue;
                }

                if (parsed.ClearsPackageSources)
                    merged.Clear();

                foreach (RawSource raw in parsed.Sources)
                {
                    if (string.IsNullOrWhiteSpace(raw.Url))
                        continue;

                    string key = string.IsNullOrWhiteSpace(raw.Key) ? raw.Url : raw.Key;
                    var definition = new NuGetSourceDefinition
                    {
                        Name = string.IsNullOrWhiteSpace(raw.Name) ? raw.Url : raw.Name,
                        Url = raw.Url,
                        IsEnabled = raw.IsEnabled,
                        Origin = file.Origin,
                        SourceConfigPath = file.Path,
                    };
                    merged[key] = definition;
                }
            }

            return merged.Values
                .OrderBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static ConfigReadResult ReadConfig(string path)
        {
            var document = XDocument.Load(path);
            var result = new ConfigReadResult();

            XElement root = document.Root;
            if (root == null)
                return result;

            XElement packageSources = root.Element("packageSources");
            XElement disabled = root.Element("disabledPackageSources");

            if (packageSources != null)
                result.ClearsPackageSources = packageSources.Element("clear") != null;

            var disabledKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (disabled != null)
            {
                if (disabled.Element("clear") != null)
                    disabledKeys.Clear();

                foreach (XElement add in disabled.Elements("add"))
                {
                    string key = (string)add.Attribute("key");
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    bool isDisabled = string.Equals((string)add.Attribute("value"), "true", StringComparison.OrdinalIgnoreCase);
                    if (isDisabled)
                        disabledKeys.Add(key);
                }
            }

            if (packageSources != null)
            {
                foreach (XElement add in packageSources.Elements("add"))
                {
                    string key = (string)add.Attribute("key");
                    string value = (string)add.Attribute("value");
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    result.Sources.Add(new RawSource
                    {
                        Key = key,
                        Name = key,
                        Url = value,
                        IsEnabled = !disabledKeys.Contains(key ?? value)
                    });
                }
            }

            return result;
        }

        private sealed class ConfigFile
        {
            public string Path;
            public NuGetSourceOrigin Origin;
        }

        private sealed class ConfigReadResult
        {
            public bool ClearsPackageSources;
            public readonly List<RawSource> Sources = new List<RawSource>();
        }

        private sealed class RawSource
        {
            public string Key;
            public string Name;
            public string Url;
            public bool IsEnabled;
        }
    }
}
