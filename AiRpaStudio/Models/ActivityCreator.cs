using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FW.EasyMvvm.Core;
using RPA.Chat.Services.Workflow;
using RPA.Helpers;

namespace RPA.Models
{
    public class ActivityCreator : BaseModel
    {
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
                OnSendMessage(ErrorText.Of(ex));
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
                OnSendMessage($"Укажи папку проекта.");
                return;
            }

            if (!Directory.Exists(SelectedPath))
            {
                OnSendMessage($"Папка {SelectedPath} не существует. Укажи существующую папку проекта.");
                return;
            }

            if (!ProjectPathResolver.IsInsideProject(SelectedPath, Properties.Settings.Default.LastLoadedProjectPath))
            {
                OnSendMessage($"Файл активности должен располагаться в папке проекта.");
                return;
            }

            if (!CreateFiles())
            {
                return;
            }

            Close();
        }

        /// <summary>
        /// Создаёт файлы активности в выбранной папке (без закрытия диалога). Возвращает <c>true</c>, если
        /// активность создана; ошибка уже отправлена через <see cref="BaseModel.OnSendMessage(string)"/>.
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
                ActivityCreateResult result = CreateActivityFiles(className);

                if (result == null || !result.Succeeded)
                {
                    OnSendMessage(result?.Message ?? "Активность не создана.");
                    return false;
                }

                // Открываем файл класса: дизайнер (.Designer.xaml) — часть библиотеки, а не процесс.
                FilePath = result.ClassFile;
                IsCreate = true;
                return true;
            }
            catch (Exception ex)
            {
                OnSendMessage(ErrorText.Of(ex));
                return false;
            }
        }

        /// <summary>
        /// Создание активности общей службой (карточка AG-08): раньше диалог копировал <c>Main.xaml</c> из
        /// рабочего каталога процесса и писал только <c>.xaml</c>. Теперь — <c>.cs</c> + дизайнер +
        /// регистрация в <c>.csproj</c>, ровно то же поведение, что у агента (<c>activity_create</c>).
        /// </summary>
        private ActivityCreateResult CreateActivityFiles(string className)
        {
            string projectFile = FindProjectFile(SelectedPath);
            if (projectFile == null)
            {
                return ActivityCreateResult.Fail(
                    $"В папке {SelectedPath} нет файла проекта (.csproj): активность создаётся только в существующем проекте библиотеки активностей.");
            }

            var request = new ActivityCreateRequest
            {
                ProjectPath = projectFile,
                ClassName = className,
                WithDesigner = true
            };

            return new ActivityCreationService().CreateAsync(request).GetAwaiter().GetResult();
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
