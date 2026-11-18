using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Agent.Mvvm;
using RPA.Helpers;
using RPA.VsProject;
using RPA.VsTemplate;

namespace RPA.Models
{
    public class ActivityCreator : BaseModel
    {
        /// <summary>Имя встроенного шаблона XAML-активности Visual Studio в манифесте сборки.</summary>
        private const string ActivityItemTemplateResourceName = "RPA.VSTemplates.Activity.Activity.xaml";

        #region MVVM

        private string selectedPath;
        private string activityName;

        public string SelectedPath
        {
            get => selectedPath;
            set
            {
                selectedPath = value;
                OnPropertyChanged();
            }
        }

        public string ActivityName
        {
            get => activityName;
            set
            {
                activityName = value;
                OnPropertyChanged();
            }
        }

        #endregion MVVM

        public bool IsCreate { get; set; }
        public string FilePath { get; set; }

        public ActivityCreator(string folderPath = null)
        {
            // Путь может прийти файлом проекта (.csproj/.sln) — например, из
            // Settings.LastLoadedProjectPath. В диалоге всегда должна быть папка проекта.
            SelectedPath = ProjectPathResolver.GetProjectFolder(folderPath ?? Properties.Settings.Default.LastLoadedProjectPath);
            ActivityName = CoerceProjectName(Consts.defaultActivityName, SelectedPath);
        }

        private string CoerceProjectName(string defaultActivityName, string selectedPath)
        {
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return $"{defaultActivityName}.xaml";
            }

            try
            {
                string[] existing = Directory.GetFiles(selectedPath)
                    .Select(Path.GetFileName)
                    .SelectMany(name => new[] { name, StripKnownExtension(name) })
                    .ToArray();

                for (int i = 0; i < 1000; i++)
                {
                    string candidate = i == 0 ? defaultActivityName : $"{defaultActivityName}{i}";
                    if (!ContainsIgnoreCase(existing, candidate + ".xaml")
                        && !ContainsIgnoreCase(existing, candidate + ".cs"))
                    {
                        return $"{candidate}.xaml";
                    }
                }
            }
            catch (Exception ex)
            {
                ShowDialogMessage(ErrorText.Of(ex));
            }

            return $"{defaultActivityName}.xaml";
        }

        public void SetFolder()
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            DialogResult result = dialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                SelectedPath = dialog.SelectedPath;
            }
        }

        public void CreateActivity()
        {
            // Нормализуем: в поле может оказаться путь к файлу проекта — тогда работаем с его папкой.
            SelectedPath = ProjectPathResolver.GetProjectFolder(SelectedPath) ?? SelectedPath?.Trim();

            if (string.IsNullOrWhiteSpace(SelectedPath))
            {
                ShowDialogMessage($"Укажи папку проекта.");
                return;
            }

            if (!Directory.Exists(SelectedPath))
            {
                ShowDialogMessage($"Папка {SelectedPath} не существует. Укажи существующую папку проекта.");
                return;
            }

            if (!ProjectPathResolver.IsInsideProject(SelectedPath, Properties.Settings.Default.LastLoadedProjectPath))
            {
                ShowDialogMessage($"Файл активности должен располагаться в папке проекта.");
                return;
            }

            if (!CreateFiles())
            {
                return;
            }

            ((System.Windows.Window)ViewModel.View)?.Close();
        }

        /// <summary>
        /// Создаёт файлы активности в выбранной папке (без закрытия диалога). Возвращает <c>true</c>, если
        /// активность создана; ошибка уже отправлена через <see cref="BaseModel.ShowDialogMessage(string)"/>.
        /// </summary>
        internal bool Create()
        {
            return CreateFiles();
        }

        private bool CreateFiles()
        {
            try
            {
                string className = NormalizeClassName(activityName);
                if (string.IsNullOrWhiteSpace(className))
                {
                    ShowDialogMessage("Введи имя активности.");
                    return false;
                }

                string xamlPath = Path.Combine(SelectedPath, className + ".xaml");
                if (File.Exists(xamlPath))
                {
                    ShowDialogMessage($"Файл {xamlPath} уже существует.");
                    return false;
                }

                string rootNamespace = ResolveRootNamespace(SelectedPath);
                string xaml = BuildActivityXaml(rootNamespace, className);

                File.WriteAllText(xamlPath, xaml, new UTF8Encoding(true));

                FilePath = xamlPath;
                IsCreate = true;
                return true;
            }
            catch (Exception ex)
            {
                ShowDialogMessage(ErrorText.Of(ex));
                return false;
            }
        }

        /// <summary>
        /// Собирает XAML новой активности из встроенного шаблона Visual Studio
        /// (<c>VSTemplates\Activity\Activity.xaml</c>) с подстановкой <c>$rootnamespace$</c>,
        /// <c>$safeitemname$</c> и <c>$targetframeworkversion$</c>.
        /// </summary>
        internal static string BuildActivityXaml(string rootNamespace, string className)
        {
            var resolver = new VsTemplateParameterResolver(new VsTemplateInstantiationRequest
            {
                RootNamespace = rootNamespace ?? string.Empty,
                SafeProjectName = className ?? string.Empty,
                SafeItemName = className ?? string.Empty,
                TargetFrameworkVersion = Consts.defaultTargetFrameworkVersion
            });

            return TemplateTextProcessor.Process(ReadActivityTemplate(), resolver.Resolve);
        }

        private static string ReadActivityTemplate()
        {
            using (Stream stream = typeof(ActivityCreator).Assembly.GetManifestResourceStream(ActivityItemTemplateResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Встроенный шаблон активности не найден: " + ActivityItemTemplateResourceName);
                }

                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// Пространство имён для <c>x:Class</c>: <c>RootNamespace</c> проекта Visual Studio, либо имя
        /// файла проекта, либо <c>RPA</c> для встроенного проекта (<c>project.json</c>).
        /// </summary>
        private static string ResolveRootNamespace(string folder)
        {
            string projectFile = FindProjectFile(folder);
            if (projectFile == null)
            {
                return "RPA";
            }

            try
            {
                string rootNamespace = VsProjectParser.Parse(projectFile).RootNamespace;
                if (!string.IsNullOrWhiteSpace(rootNamespace))
                {
                    return rootNamespace;
                }
            }
            catch (Exception)
            {
                // Битый .csproj не должен мешать созданию активности: fallback на имя файла проекта.
            }

            return Path.GetFileNameWithoutExtension(projectFile);
        }

        /// <summary>Файл проекта в папке активности: первый по имени, чтобы поведение было предсказуемым.</summary>
        private static string FindProjectFile(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return null;
            }

            return Directory.GetFiles(folder, "*.csproj")
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        /// <summary>Имя из поля диалога (<c>Activity.xaml</c>) превращается в имя класса (<c>Activity</c>).</summary>
        internal static string NormalizeClassName(string name)
        {
            string value = (name ?? string.Empty).Replace(" ", string.Empty);
            return StripKnownExtension(value);
        }

        private static string StripKnownExtension(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            foreach (string extension in new[] { ".xaml", ".cs" })
            {
                if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return name.Substring(0, name.Length - extension.Length);
                }
            }

            return name;
        }

        private static bool ContainsIgnoreCase(IEnumerable<string> values, string value)
        {
            return values.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        }
    }
}