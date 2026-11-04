using System;
using System.Activities.Core.Presentation;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using FW.EasyMvvm.Core;
using RPA.Core;
using RPA.Core.Generation;
using RPA.Enums;
using RPA.Helpers;
using RPA.ViewModels;
using RPA.Views;
using RPA.VsProject;
using Agent.Core.Services;
using RpaCommon;

namespace RPA.Models
{
    public class Main : BaseModel
    {
        #region MVVM

        #region Fields

        private string language;

        /// <summary>
        /// Идёт восстановление сессии: изменения набора вкладок в этот момент не сохраняем, иначе
        /// промежуточное состояние (открыта только главная активность) затрёт сохранённый список.
        /// </summary>
        private bool _isRestoringSession;

        #endregion Fields

        public Main()
        {
            // Состояние вкладок сохраняется в одном месте и для обоих режимов проекта: классический
            // project.json (CustomWfDesigner.LoadProject) и проект Visual Studio (.sln/.csproj).
            CustomWfDesigner.WorkflowDesigners.CollectionChanged += WorkflowTabs_CollectionChanged;
            OpenCodeDocuments.CollectionChanged += WorkflowTabs_CollectionChanged;
        }

        private void WorkflowTabs_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            SaveSessionState();
        }

        #region Properties
        public CustomWfDesigner CustomWfDesigner { get; } = CustomWfDesigner.GetInstance();

        public ProjectManager ProjectManager { get; set; } = ProjectManager.GetInstance();

        public ObservableCollection<CodeDocument> OpenCodeDocuments { get; } = new ObservableCollection<CodeDocument>();

        public string Language
        {
            get => language;
            set
            {
                language = value;
                CultureInfo culture = value == "RU" ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.GetCultureInfo("en-US");
                App.Language = culture;
                Properties.Settings.Default.Language = value;
                Properties.Settings.Default.Save();
                OnPropertyChanged();
            }
        }

        #endregion Properties

        #endregion MVVM

        public override void Run()
        {
            //associates all of the basic activities with their designers, call only here
            new DesignerMetadata().Register();
            SetLanguage();
            _ = AppSettings.GetInstance();
        }

        private void SetLanguage()
        {
            string lang = Properties.Settings.Default.Language;

            if (string.IsNullOrEmpty(lang))
            {
                //  автоматическая установка языка по культуре
                CultureInfo ci = CultureInfo.CurrentCulture;
                Language = ci.Name == "ru-RU" ? "RU" : "EN";
            }
            else
            {
                Language = lang;
            }
        }

        public override void Loaded()
        {
            string lastProjectPath = Properties.Settings.Default.LastLoadedProjectPath;

            if (string.IsNullOrWhiteSpace(lastProjectPath))
            {
                CreateHelloWorldProject();
                lastProjectPath = Properties.Settings.Default.LastLoadedProjectPath;
            }

            if (string.IsNullOrWhiteSpace(lastProjectPath))
            {
                return;
            }

            if (IsVsProjectPath(lastProjectPath))
            {
                LoadVsProjectIntoStudio(lastProjectPath);
            }
            else
            {
                LoadProject(lastProjectPath);
            }
        }

        /// <summary>
        /// Determines whether a stored project path points to a Visual Studio
        /// solution/project (.sln/.csproj) instead of a simple RPA project folder.
        /// </summary>
        internal static bool IsVsProjectPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
        }

        private void CreateHelloWorldProject()
        {
            ProjectCreator projectCreator = new ProjectCreator(ProjectType.Process);
            projectCreator.Project.Name = "Hello world";
            //create if not exist
            if (!Directory.Exists(Path.Combine(projectCreator.SelectedPath, projectCreator.Project.Name)))
            {
                projectCreator.CreateProject();
                Properties.Settings.Default.LastLoadedProjectPath = projectCreator.RootFolder;
                Properties.Settings.Default.Save();
            }
        }

        private void LoadProject(string rootProjectFolder)
        {
            if (string.IsNullOrWhiteSpace(rootProjectFolder))
            {
                return;
            }

            if (!Directory.Exists(rootProjectFolder) || !File.Exists(Path.Combine(rootProjectFolder, Consts.projectFileName)))
            {
                ClearInvalidLastProjectPath(rootProjectFolder);
                return;
            }

            try
            {
                CallResult resp = ProjectManager.InitFileTree(rootProjectFolder);
                if (!resp.Success)
                {
                    OnSendMessage(resp.Error.Message);
                    ClearInvalidLastProjectPath(rootProjectFolder);
                    return;
                }

                _isRestoringSession = true;
                try
                {
                    CustomWfDesigner.LoadProject(rootProjectFolder);
                    Properties.Settings.Default.LastLoadedProjectPath = rootProjectFolder;
                    Properties.Settings.Default.Save();

                    // Порядок вкладок, вкладки редактора кода и активная вкладка — из сохранённой сессии.
                    RestoreSessionState(rootProjectFolder);
                }
                finally
                {
                    _isRestoringSession = false;
                }

                SaveSessionState();
            }
            catch (Exception ex)
            {
                CustomWfDesigner.GetInstance().Log($"Ошибка загрузки проекта: {ex}", (int)RpaCommon.Enums.LogLevel.Error);
            }
        }

        #region Commands

        #region Files

        /// <summary>
        /// Add New Activity To Project
        /// </summary>
        public void CmdAddNewActivityToProject()
        {
            // В настройке может лежать файл проекта (.csproj/.sln) — диалогу нужна папка.
            string projectFolder = ProjectPathResolver.GetProjectFolder(Properties.Settings.Default.LastLoadedProjectPath)
                                   ?? ProjectPathResolver.GetProjectFolder(ProjectManager.ProjectFolder);
            ActivityCreator activityCreator = new ActivityCreator(projectFolder);
            MvvmFactory.CreateWindow(activityCreator, new ActivityCreatorViewModel(), new ActivityCreatorView(), ViewMode.ShowDialog);
            if (activityCreator.IsCreate)
            {
                OpenCreatedActivity(activityCreator.FilePath);
            }
        }

        /// <summary>
        /// Add New Activity To Project
        /// </summary>
        public void CmdAddNewActivityToProjectFolder(string folderPath)
        {
            // В команду может прийти путь узла дерева: для проекта Visual Studio это файл .csproj.
            ActivityCreator activityCreator = new ActivityCreator(folderPath);
            MvvmFactory.CreateWindow(activityCreator, new ActivityCreatorViewModel(), new ActivityCreatorView(), ViewMode.ShowDialog);
            if (activityCreator.IsCreate)
            {
                OpenCreatedActivity(activityCreator.FilePath);
            }
        }

        /// <summary>
        /// Открывает созданную активность, добавляет узел в дерево проекта и регистрирует файл
        /// в .csproj — иначе новая активность не попадёт в сборку библиотеки Visual Studio.
        /// </summary>
        private void OpenCreatedActivity(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            // Открытие — через DesignerOpenService (единственный путь показа вкладки, этап 3 плана):
            // второй конструктор на уже открытый файл не создаётся, ошибка возвращается результатом.
            DesignerOpenResult openResult = DesignerOpenService.ForStudio(message => CustomWfDesigner.Log(message)).OpenOrActivate(filePath);
            if (!openResult.Ok)
            {
                CustomWfDesigner.Log(openResult.Describe(), (int)RpaCommon.Enums.LogLevel.Error);
            }


            ProjectManager.AddFile(filePath);

            CallResult registerResult = ProjectManager.RegisterFileInVsProject(filePath);
            if (registerResult.Error != null)
            {
                OnSendMessage(registerResult.Error.Message);
            }
        }

        /// <summary>
        /// Открывает панель NuGet для указанного проекта — пункт «Добавить зависимость».
        /// </summary>
        public void CmdOpenNuGetPanel(string projectPath)
        {
            (ViewModel?.View as MainView)?.ShowNuGetPanelFor(projectPath);
        }

        public void CmdRemoveDesigner(string loadedFile)
        {
            CustomWfDesigner.RemoveDesignerByLoadedFile(loadedFile);
        }

        public void CmdSetActiveDesigner(string loadedFile)
        {
            _ = CustomWfDesigner.SetActiveDesigner(loadedFile);
        }

        /// <summary>
        /// Load activity or other
        /// </summary>
        /// <param name="filePath"></param>
        public void CmdTryLoadFile(string filePath)
        {
            if (filePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    DesignerOpenResult openResult = DesignerOpenService.ForStudio(message => CustomWfDesigner.Log(message)).OpenOrActivate(filePath);
                    if (!openResult.Ok)
                    {
                        // Ошибка открытия — сообщение пользователю с полным текстом, а не исключение:
                        // файл может быть занят, недоступен на запись или не быть процессом вовсе.
                        string message = LocalizeHelper.Get("lang_msg_xaml_load_failed", string.Join(Environment.NewLine, openResult.Errors));
                        CustomWfDesigner.Log(message, (int)RpaCommon.Enums.LogLevel.Error);
                        OnSendMessage(message);
                    }
                }
                catch (Exception ex)
                {
                    CustomWfDesigner.Log(LocalizeHelper.Get("lang_msg_xaml_load_failed", ErrorText.Of(ex)), (int)RpaCommon.Enums.LogLevel.Error);
                    OnSendMessage(LocalizeHelper.Get("lang_msg_xaml_load_failed", ErrorText.Of(ex)));
                }
            }
            else if (filePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                     || filePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                CmdVsOpenInVisualStudio(filePath);
            }
            else if (CodeLanguageProvider.GetLanguageForExtension(filePath) != null)
            {
                CmdOpenInCodeEditor(filePath);
            }
            else
            {
                try
                {
                    _ = Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    OnSendMessage(LocalizeHelper.Get("lang_msg_open_file_error", ErrorText.Of(ex)));
                }
            }
        }

        /// <summary>
        /// Opens a file in the built-in code editor (XAML/C#/JSON/XML).
        /// </summary>
        public void CmdOpenInCodeEditor(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            if (!File.Exists(filePath))
            {
                OnSendMessage(LocalizeHelper.Get("lang_msg_open_file_error", "File not found"));
                return;
            }

            CodeDocument existing = OpenCodeDocuments.FirstOrDefault(
                d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                (ViewModel?.View as MainView)?.ActivateCodeDocument(filePath);
                return;
            }

            OpenCodeDocuments.Add(CodeDocument.Load(filePath));
        }

        /// <summary>
        /// Closes a code document opened in the built-in editor.
        /// </summary>
        public void CmdCloseCodeDocument(string filePath)
        {
            CodeDocument document = OpenCodeDocuments.FirstOrDefault(
                d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (document != null)
            {
                _ = OpenCodeDocuments.Remove(document);
            }
        }

        /// <summary>
        /// Load project
        /// Хотя указываем файл, на самом деле загрузка происходит по пути к папке
        /// </summary>
        /// <param name="filePath"></param>
        public void CmdTryLoadProject()
        {
            string projFilePath = IOHelper.GetFilePath("Json files (*.json)|*.json");
            if (string.IsNullOrWhiteSpace(projFilePath))
            {
                return;
            }
            string dirPath = Path.GetDirectoryName(projFilePath);
            LoadProject(dirPath);
        }

        /// <summary>
        /// Create project process type
        /// </summary>
        public void CmdWorkflowNewProcess()
        {
            ProjectCreator projectCreator = new ProjectCreator(ProjectType.Process);
            MvvmFactory.CreateWindow(projectCreator, new ProjectCreatorViewModel(), new ProjectCreatorView(), ViewMode.ShowDialog);
            if (projectCreator.IsCreate)
            {
                LoadProject(projectCreator.RootFolder);
            }
        }

        /// <summary>
        /// Create project library type
        /// </summary>
        public void CmdWorkflowNewLibrary()
        {
            MvvmFactory.CreateWindow(new ProjectCreator(ProjectType.Library), new ProjectCreatorViewModel(), new ProjectCreatorView(), ViewMode.ShowDialog);
        }

        /// <summary>
        /// Creates a new Workflow Designer instance and loads the Default Workflow with C# Expression Editor
        /// </summary>
        public void CmdWorkflowNewCSharp()
        {
            CustomWfDesigner.WorkflowNewCSharp();
        }

        /// <summary>
        /// Save the current state of a Workflow
        /// </summary>
        public void CmdWorkflowSave()
        {
            CustomWfDesigner.WorkflowSave();
        }

        /// <summary>
        /// Save all modified workflows
        /// </summary>
        public void CmdWorkflowSaveAll()
        {
            CustomWfDesigner.WorkflowSaveAll();
        }

        /// <summary>
        /// Save as the current state of a Workflow
        /// </summary>
        public void CmdWorkflowSaveAs()
        {
            CustomWfDesigner.WorkflowSaveAs();
        }

        /// <summary>
        /// Loads a Workflow into a new Workflow Designer instance
        /// </summary>
        public void CmdWorkflowOpen()
        {
            CustomWfDesigner.WorkflowOpen();
        }

        #endregion Files

        /// <summary>
        /// Creates a new Workflow Application instance and executes the Current Workflow
        /// </summary>
        public void CmdWorkflowRun()
        {
            CustomWfDesigner.WorkflowRun();
        }

        public void CmdWorkflowPause()
        {
            CustomWfDesigner.WorkflowPause();
        }

        /// <summary>
        /// Stops the Current Workflow
        /// </summary>
        public void CmdWorkflowStop()
        {
            CustomWfDesigner.WorkflowStop();
        }

        public void CmdUndo()
        {
            CustomWfDesigner.CmdUndo();
        }

        public void CmdRedo()
        {
            CustomWfDesigner.CmdRedo();
        }

        #region Visual Studio

        /// <summary>
        /// Create WF Activity Library from Visual Studio template
        /// </summary>
        public void CmdVsNewProjectFromTemplate()
        {
            VsTemplateProjectCreator creator = new VsTemplateProjectCreator();
            MvvmFactory.CreateWindow(creator, new VsTemplateProjectCreatorViewModel(), new VsTemplateProjectCreatorView(), ViewMode.ShowDialog);
            if (creator.IsCreate)
            {
                OfferVsProjectActions(creator);
            }
        }

        /// <summary>
        /// Open a .sln/.csproj file in an external Visual Studio instance (always a new instance)
        /// </summary>
        /// <param name="path"></param>
        public void CmdVsOpenInVisualStudio(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                OnSendMessage(LocalizeHelper.Get("lang_msg_specify_path"));
                return;
            }

            try
            {
                VsProjectOpener.OpenInVisualStudio(path);
            }
            catch (Exception ex)
            {
                CustomWfDesigner.Log("Ошибка открытия в Visual Studio: " + ErrorText.Of(ex), (int)RpaCommon.Enums.LogLevel.Error);
                OnSendMessage(LocalizeHelper.Get("lang_msg_open_vs_error", ErrorText.Of(ex)));
            }
        }

        /// <summary>
        /// Opens a .sln/.csproj file (dialog) and either opens it in VS or loads it into the studio.
        /// </summary>
        public void CmdVsOpenProject()
        {
            VsProjectOpen model = new VsProjectOpen();
            MvvmFactory.CreateWindow(model, new VsProjectOpenViewModel(), new VsProjectOpenView(), ViewMode.ShowDialog);
            if (model.IsLoadIntoStudio)
            {
                LoadVsProjectIntoStudio(model.FilePath);
            }
        }

        /// <summary>
        /// Builds a .sln/.csproj project via MSBuild and logs the output.
        /// </summary>
        public void CmdVsBuild(string path)
        {
            CmdVsBuild(path, MsBuildLocator.ParseToolchain(Properties.Settings.Default.MsBuildToolchain));
        }

        /// <summary>
        /// Builds a .sln/.csproj project with an explicitly chosen MSBuild toolchain and logs the selected
        /// MSBuild.exe, its version and any fallback, so "which Visual Studio built this" is always visible.
        /// </summary>
        public void CmdVsBuild(string path, MsBuildToolchain toolchain)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                OnSendMessage(LocalizeHelper.Get("lang_msg_specify_path"));
                return;
            }

            CustomWfDesigner.Log(LocalizeHelper.Get("lang_msg_build_started", path), (int)RpaCommon.Enums.LogLevel.Info);

            VsBuildResult result = VsProjectBuilder.BuildWithToolchain(
                path,
                "Debug",
                toolchain,
                message => CustomWfDesigner.Log(message));

            if (result.Success)
            {
                CustomWfDesigner.Log(LocalizeHelper.Get("lang_msg_build_success"), (int)RpaCommon.Enums.LogLevel.Info);
            }
            else
            {
                CustomWfDesigner.Log(LocalizeHelper.Get("lang_msg_build_failed", result.ExitCode), (int)RpaCommon.Enums.LogLevel.Error);
            }
        }

        private void LoadVsProjectIntoStudio(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!File.Exists(path))
            {
                ClearInvalidLastProjectPath(path);
                return;
            }

            try
            {
                CallResult resp = ProjectManager.InitVsProjectTree(path);
                if (!resp.Success)
                {
                    OnSendMessage(resp.Error.Message);
                    return;
                }

                Properties.Settings.Default.LastLoadedProjectPath = path;
                Properties.Settings.Default.Save();

                // Проект Visual Studio не открывал ни одного документа: сохранение вкладок подписывалось
                // только внутри CustomWfDesigner.LoadProject (классический проект), поэтому при перезапуске
                // вкладок не было, хотя при закрытии они были.
                _isRestoringSession = true;
                try
                {
                    RestoreSessionState(path);
                }
                finally
                {
                    _isRestoringSession = false;
                }

                SaveSessionState();

                CustomWfDesigner.Log(LocalizeHelper.Get("lang_msg_vs_project_loaded", path), (int)RpaCommon.Enums.LogLevel.Info);
            }
            catch (Exception ex)
            {
                CustomWfDesigner.Log(LocalizeHelper.Get("lang_msg_vs_project_load_error", ErrorText.Of(ex)), (int)RpaCommon.Enums.LogLevel.Error);
                OnSendMessage(LocalizeHelper.Get("lang_msg_vs_project_load_error", ErrorText.Of(ex)));
            }
        }

        /// <summary>
        /// Сохраняет состояние вкладок студии: вкладки дизайнера в их порядке, вкладки редактора кода и
        /// активную вкладку (<see cref="WorkflowSessionState"/>). Настройка — <c>DesignerFiles</c>
        /// (в ней же лежал старый формат — JSON-массив путей).
        /// </summary>
        internal void SaveSessionState()
        {
            if (_isRestoringSession)
            {
                return;
            }

            try
            {
                string projectPath = Properties.Settings.Default.LastLoadedProjectPath;
                if (string.IsNullOrWhiteSpace(projectPath))
                {
                    return;
                }

                List<string> workflowFiles = CustomWfDesigner.WorkflowDesigners
                    .Select(CustomWfDesigner.GetLoadedFile)
                    .Where(file => !string.IsNullOrWhiteSpace(file))
                    .ToList();

                List<string> codeFiles = OpenCodeDocuments
                    .Select(document => document.FilePath)
                    .Where(file => !string.IsNullOrWhiteSpace(file))
                    .ToList();

                WorkflowSessionState state = WorkflowSessionState.Create(
                    projectPath, workflowFiles, codeFiles, ActiveDocumentPath());

                Properties.Settings.Default.DesignerFiles = state.Serialize();
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                CustomWfDesigner.Log($"Не удалось сохранить состояние вкладок: {ErrorText.Of(ex)}", (int)RpaCommon.Enums.LogLevel.Warn);
            }
        }

        /// <summary>Путь активной вкладки: активный дизайнер, иначе последняя вкладка редактора кода.</summary>
        private string ActiveDocumentPath()
        {
            string activeFile = CustomWfDesigner.GetLoadedFile(CustomWfDesigner.ActiveWorkflowDesignerEx);
            if (!string.IsNullOrWhiteSpace(activeFile))
            {
                return activeFile;
            }

            return OpenCodeDocuments.LastOrDefault()?.FilePath;
        }

        /// <summary>
        /// Восстанавливает сохранённые вкладки: открывает документы тем же путём, что и дерево проекта
        /// (<see cref="CmdTryLoadFile"/>), возвращает порядок вкладок дизайнера и активирует сохранённую.
        /// Несуществующие файлы и файлы чужого проекта отбрасывает <see cref="WorkflowSessionState"/>.
        /// </summary>
        internal void RestoreSessionState(string projectPath)
        {
            try
            {
                WorkflowSessionState state = WorkflowSessionState.Deserialize(
                    Properties.Settings.Default.DesignerFiles, projectPath);

                if (state.Documents.Count == 0)
                {
                    return;
                }

                foreach (WorkflowSessionDocument document in state.Documents)
                {
                    if (IsDocumentOpen(document.FilePath))
                    {
                        continue;
                    }

                    // Неизвестное расширение открывалось бы внешним приложением — такого при
                    // восстановлении сессии быть не должно.
                    if (!document.IsWorkflow && CodeLanguageProvider.GetLanguageForExtension(document.FilePath) == null)
                    {
                        continue;
                    }

                    try
                    {
                        CmdTryLoadFile(document.FilePath);
                    }
                    catch (Exception ex)
                    {
                        CustomWfDesigner.Log($"Не удалось восстановить вкладку {document.FilePath}: {ErrorText.Of(ex)}", (int)RpaCommon.Enums.LogLevel.Warn);
                    }
                }

                CustomWfDesigner.ApplyDocumentOrder(state.Documents.Where(d => d.IsWorkflow).Select(d => d.FilePath));

                if (!string.IsNullOrWhiteSpace(state.ActiveDocument))
                {
                    ActivateDocument(state.ActiveDocument);
                }
            }
            catch (Exception ex)
            {
                CustomWfDesigner.Log($"Не удалось восстановить состояние вкладок: {ErrorText.Of(ex)}", (int)RpaCommon.Enums.LogLevel.Warn);
            }
        }

        private bool IsDocumentOpen(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return true;
            }

            return CustomWfDesigner.WorkflowDesigners.Any(
                       d => string.Equals(CustomWfDesigner.GetLoadedFile(d), filePath, StringComparison.OrdinalIgnoreCase))
                   || OpenCodeDocuments.Any(
                       d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        }

        private void ActivateDocument(string filePath)
        {
            if (CustomWfDesigner.WorkflowDesigners.Any(
                    d => string.Equals(CustomWfDesigner.GetLoadedFile(d), filePath, StringComparison.OrdinalIgnoreCase)))
            {
                _ = CustomWfDesigner.SetActiveDesigner(filePath);
                return;
            }

            (ViewModel?.View as MainView)?.ActivateCodeDocument(filePath);
        }
        private static void ClearInvalidLastProjectPath(string invalidPath)
        {
            if (string.IsNullOrWhiteSpace(invalidPath))
            {
                return;
            }

            if (Properties.Settings.Default.LastLoadedProjectPath == invalidPath)
            {
                Properties.Settings.Default.LastLoadedProjectPath = string.Empty;
                Properties.Settings.Default.Save();
            }
        }

        private void OfferVsProjectActions(VsTemplateProjectCreator creator)
        {
            MessageBoxResult choice = MessageBox.Show(
                creator.ProjectFilePath,
                LocalizeHelper.Get("lang_vs_project_created"),
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Yes);

            switch (choice)
            {
                case MessageBoxResult.Yes:
                    CmdVsOpenInVisualStudio(creator.ProjectFilePath);
                    break;
                case MessageBoxResult.No:
                    LoadVsProjectIntoStudio(creator.ProjectFilePath);
                    break;
                case MessageBoxResult.None:
                    break;
                case MessageBoxResult.OK:
                    break;
                case MessageBoxResult.Cancel:
                    break;
                default:
                    break;
            }
        }

        #endregion Visual Studio

        #endregion Commands
    }
}
