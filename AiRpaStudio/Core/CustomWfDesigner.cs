using System;
using System.Activities.Presentation;
using System.Activities.Presentation.Debug;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using ActivityLibraryBase.BaseActivities;
using FW.EasyMvvm.Core;
using LogControlLibrary.Controls;
using Microsoft.Win32;
using Newtonsoft.Json;
using RPA.Entities;
using RPA.Enums;
using RPA.Core.Workflow;
using RPA.Core.Generation;

using RPA.Helpers;
using RPA.Views;
using RpaCommon;

namespace RPA.Core
{
    public class CustomWfDesigner : BaseModel
    {
        #region Properties

        public CustomWfDesignerState CustomWfDesignerState
        {
            get => _customWfDesignerState;
            set
            {
                _customWfDesignerState = value;
                OnPropertyChanged();
                UpdateCanSaveStates();
            }
        }

        public ObservableCollection<WorkflowDesignerEx> WorkflowDesigners { get; set; } = new ObservableCollection<WorkflowDesignerEx>();

        public WorkflowDesignerEx ActiveWorkflowDesignerEx
        {
            get => _activeWorkflowDesigner;
            set
            {
                _activeWorkflowDesigner = value;
                // DocGroupHelper создаётся в MainView_Loaded: до этого вкладок ещё нет и выбирать нечего.
                DocGroupHelper.GetInstance()?.SetSelectedTab(_activeWorkflowDesigner);
                OnPropertyChanged();
                UpdateCanSaveStates();
            }
        }

        public bool CanSave
        {
            get => _canSave;
            private set
            {
                _canSave = value;
                OnPropertyChanged();
            }
        }

        public bool CanSaveAll
        {
            get => _canSaveAll;
            private set
            {
                _canSaveAll = value;
                OnPropertyChanged();
            }
        }

        public WorkflowDesignerEx RootWorkflowDesigner { get; private set; }

        public LogViewerControl LogViewerControl => ((MainView)ViewModel.View).LogViewerControl;

        public Project Project { get; private set; }

        #endregion Properties

        #region Fields

        private CustomWfDesignerState _customWfDesignerState = CustomWfDesignerState.Stop;
        private WorkflowDesignerEx _activeWorkflowDesigner;
        private const string _defaultWorkflowFileName = "Activity1.xaml";
        //private readonly ConsoleWriter _consoleWriter = new ConsoleWriter();
        private ActivityRunner _activityRunner;
        private bool _canSave;
        private bool _canSaveAll;
        private WorkflowTypeRegistry _looseTypeRegistry;

        #endregion Fields

        #region Singleton

        private static readonly CustomWfDesigner instance = new CustomWfDesigner();

        private CustomWfDesigner()
        {
            //_consoleWriter.WriteEvent += ConsoleWriter_WriteEvent;
            //_consoleWriter.WriteLineEvent += ConsoleWriter_WriteEvent;
            //Console.SetOut(_consoleWriter);

            WorkflowDesignerIcons.UseWindowsStoreAppStyleIcons();
            ActivityLogger.OutputAction = Log;

            WorkflowDesigners.CollectionChanged += WorkflowDesigners_CollectionChanged_EditTracking;
        }

        /// <summary>
        /// Файл, загруженный во вкладку дизайнера, или <c>null</c>, если вкладка ещё без файла.
        /// <para>
        /// Сохранение состояния вкладок переехало в <c>Main.SaveSessionState</c>: там видны и вкладки
        /// дизайнера, и вкладки редактора кода, и активная вкладка. Старый обработчик подписывался только
        /// внутри <see cref="LoadProject"/>, поэтому в проекте Visual Studio (<c>.sln</c>/<c>.csproj</c>)
        /// состояние не сохранялось вовсе.
        /// </para>
        /// </summary>
        public static string GetLoadedFile(WorkflowDesignerEx designer)
        {
            try
            {
                return designer?.CurrentWorkflowFile;
            }
            catch (Exception)
            {
                // Вкладка без WorkflowFileItem (дизайнер создан, файл ещё не загружен).
                return null;
            }
        }

        /// <summary>
        /// Приводит вкладки дизайнера к сохранённому порядку. Нужно после восстановления сессии: главная
        /// активность проекта открывается первой, а пользователь мог оставить её, например, третьей.
        /// </summary>
        internal void ApplyDocumentOrder(System.Collections.Generic.IEnumerable<string> order)
        {
            if (order == null)
            {
                return;
            }

            int index = 0;
            foreach (string file in order)
            {
                if (string.IsNullOrWhiteSpace(file))
                {
                    continue;
                }

                try
                {
                    WorkflowDesignerEx designer = GetDesignerByFile(file);
                    if (designer == null)
                    {
                        continue;
                    }

                    int current = WorkflowDesigners.IndexOf(designer);
                    if (current >= 0 && current != index)
                    {
                        WorkflowDesigners.Move(current, index);
                    }

                    index++;
                }
                catch (Exception ex)
                {
                    Log($"Не удалось переставить вкладку {file}: {ErrorText.Of(ex)}", (int)RpaCommon.Enums.LogLevel.Warn);
                }
            }
        }

        private void WorkflowDesigners_CollectionChanged_EditTracking(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Subscribe to new designers
            if (e.NewItems != null)
            {
                foreach (WorkflowDesignerEx designer in e.NewItems)
                {
                    designer.PropertyChanged += Designer_PropertyChanged;
                }
            }

            // Unsubscribe from removed designers
            if (e.OldItems != null)
            {
                foreach (WorkflowDesignerEx designer in e.OldItems)
                {
                    designer.PropertyChanged -= Designer_PropertyChanged;
                }
            }

            UpdateCanSaveStates();
        }

        private void Designer_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WorkflowDesignerEx.IsEdited))
            {
                UpdateCanSaveStates();
            }
        }

        private void UpdateCanSaveStates()
        {
            bool isStopped = CustomWfDesignerState == CustomWfDesignerState.Stop;

            // CanSave: stopped AND active designer is edited
            CanSave = isStopped &&
                      ActiveWorkflowDesignerEx != null &&
                      ActiveWorkflowDesignerEx.IsEdited;

            // CanSaveAll: stopped AND any designer is edited
            CanSaveAll = isStopped &&
                         WorkflowDesigners.Any(d => d.IsEdited);
        }

        public static CustomWfDesigner GetInstance()
        {
            return instance;
        }

        #endregion Singleton

        #region Log

        public void Log(string message, int logLevel = 0)
        {
            LogViewerControl.AddLog((LogControlLibrary.Models.LogLevel)logLevel, message);
        }

        #endregion Log

        #region Commands

        /// <summary>
        /// Creates a new Workflow Application instance and executes the Current Workflow
        /// </summary>
        public void WorkflowRun()
        {
            try
            {
                CustomWfDesignerState = CustomWfDesignerState.Run;

                if (ActiveWorkflowDesignerEx == null)
                {
                    return;
                }

                if (_activityRunner is null || _activityRunner.WorkflowState == WorkflowState.Complete || _activityRunner.WorkflowState == WorkflowState.Aborted)
                {
                    // запуск нового workflow

                    ((MainView)ViewModel.View).Dispatcher.Invoke(() =>
                    {
                        LogViewerControl.ClearLogs();
                        RootWorkflowDesigner = ActiveWorkflowDesignerEx;
                    });

                    _activityRunner = new ActivityRunner();
                    _activityRunner.PropertyChanged += ActivityRunner_PropertyChanged;
                    _activityRunner.Run(RootWorkflowDesigner.CurrentWorkflowFile);
                }
                else
                {
                    // продолжить с точки останова
                    _activityRunner.Run();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                CustomWfDesignerState = CustomWfDesignerState.Stop;
            }
        }

        private void ActivityRunner_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "WorkflowState" && (((ActivityRunner)sender).WorkflowState == WorkflowState.Aborted
                || ((ActivityRunner)sender).WorkflowState == WorkflowState.StartingError
                || ((ActivityRunner)sender).WorkflowState == WorkflowState.Complete))
            {
                CustomWfDesignerState = CustomWfDesignerState.Stop;
            }
        }

        /// <summary>
        /// Creates a new Workflow Designer instance and loads the Default Workflow with C# Expression Editor
        /// </summary>
        public void WorkflowNewCSharp()
        {
            ActiveWorkflowDesignerEx = LoadActivityToDesignerHelper.LoadActivityToDesigner(_defaultWorkflowFileName);
        }

        internal void WorkflowPause()
        {
            CustomWfDesignerState = CustomWfDesignerState.Pause;
            _activityRunner?.WorkflowPause();
        }

        /// <summary>
        /// Workflow Stop
        /// </summary>
        public void WorkflowStop()
        {
            //manual stop
            CustomWfDesignerState = CustomWfDesignerState.Stop;
            _activityRunner?.WorkflowStop();
        }

        public void CmdUndo()
        {
            _ = ActiveWorkflowDesignerEx.Context.Services.GetService<UndoEngine>().Undo();
        }

        public void CmdRedo()
        {
            _ = ActiveWorkflowDesignerEx.Context.Services.GetService<UndoEngine>().Redo();
        }

        /// <summary>
        /// Loads a Workflow into a new Workflow Designer instance
        /// </summary>
        public void WorkflowOpen()
        {
            OpenFileDialog dialogOpen = new OpenFileDialog
            {
                Title = "Open Workflow",
                Filter = "Workflows (.xaml)|*.xaml"
            };

            if (dialogOpen.ShowDialog() == true)
            {
                // Файл нельзя держать открытым на чтение во время LoadActivityToDesigner: он в конце
                // вызывает WorkflowDesigner.Save(sourceFile), а StreamReader(path, true) открывает
                // FileStream с FileShare.Read — запись запрещена, и Save падал с IOException
                // «файл используется другим процессом». Поток чтения открывался в using и освобождался
                // только после возврата из LoadActivityToDesigner, то есть уже после Save.
                try
                {
                    // Открытие — через DesignerOpenService: второй конструктор на уже открытый файл
                    // не создаётся, вызов уходит на UI-поток, ошибка возвращается результатом
                    // (этап 3 плана `конвейер генерации XAML`).
                    DesignerOpenResult openResult = DesignerOpenService.ForStudio(message => Log(message)).OpenOrActivate(dialogOpen.FileName);
                    if (!openResult.Ok)
                    {
                        Log(openResult.Describe(), (int)RpaCommon.Enums.LogLevel.Error);
                    }
                }
                catch (Exception ex)
                {
                    // Открытие воркфлоу не должно ронять приложение: файл может быть занят другим
                    // процессом, недоступен на запись и т.п. — сообщаем пользователю в лог/консоль.
                    Log($"Не удалось открыть {dialogOpen.FileName}: {ErrorText.Of(ex)}", (int)RpaCommon.Enums.LogLevel.Error);
                }
            }
        }

        /// <summary>
        /// Сохраняет воркфлоу в файл и не даёт исключению записи выйти наружу: занятый другим
        /// процессом файл или файл только для чтения — это ошибка пользователя, а не падение студии.
        /// Возвращает <c>true</c>, если файл успешно записан.
        /// </summary>
        private bool TrySaveDesigner(WorkflowDesignerEx designer, string filePath)
        {
            try
            {
                designer.Save(filePath);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log($"Не удалось сохранить {filePath}: файл занят другим процессом или доступен только для чтения — {ErrorText.Of(ex)}", (int)RpaCommon.Enums.LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Save the current state of a Workflow
        /// </summary>
        public void WorkflowSave()
        {
            if (ActiveWorkflowDesignerEx.CurrentWorkflowFile == string.Empty)
            {
                SaveFileDialog dialogSave = new SaveFileDialog
                {
                    Title = "Save Workflow",
                    Filter = "Workflows (.xaml)|*.xaml",
                    FileName = "Workflow"
                };

                if (dialogSave.ShowDialog() == true)
                {
                    if (!TrySaveDesigner(ActiveWorkflowDesignerEx, dialogSave.FileName))
                    {
                        return;
                    }
                    // Сохраняем точки останова
                    string breakPointsFile = $"{dialogSave.FileName}.breakpoints";
                    SaveBreakpoints(ActiveWorkflowDesignerEx, breakPointsFile);
                    ActiveWorkflowDesignerEx.IsEdited = false;
                }
            }
            else
            {
                string loadedFile = ActiveWorkflowDesignerEx.Context.Items.GetValue<WorkflowFileItem>().LoadedFile;
                string breakPointsFile = $"{loadedFile}.breakpoints";
                if (!TrySaveDesigner(ActiveWorkflowDesignerEx, loadedFile))
                {
                    return;
                }

                // Сохраняем точки останова
                SaveBreakpoints(ActiveWorkflowDesignerEx, breakPointsFile);
                ActiveWorkflowDesignerEx.IsEdited = false;
            }
        }

        /// <summary>
        /// Save all modified workflows
        /// </summary>
        public void WorkflowSaveAll()
        {
            foreach (WorkflowDesignerEx designer in WorkflowDesigners.Where(d => d.IsEdited))
            {
                string loadedFile = designer.CurrentWorkflowFile;
                if (!string.IsNullOrEmpty(loadedFile))
                {
                    if (!TrySaveDesigner(designer, loadedFile))
                    {
                        continue;
                    }
                    string breakPointsFile = $"{loadedFile}.breakpoints";
                    SaveBreakpoints(designer, breakPointsFile);
                    designer.IsEdited = false;
                }
            }
        }

        // Метод для сохранения точек останова
        public void SaveBreakpoints(WorkflowDesigner designer, string breakPointsFile)
        {
            try
            {
                DebuggerService debuggerService = (DebuggerService)designer.DebugManagerView;
                if (debuggerService != null)
                {
                    IDictionary<System.Activities.Debugger.SourceLocation, BreakpointTypes> breakpoints = debuggerService.GetBreakpointLocations();
                    if (breakpoints != null && breakpoints.Count > 0)
                    {
                        // Конвертируем SourceLocations в сериализуемый формат
                        var breakpointData = breakpoints
                            .Select(bp => new
                            {
                                bp.Key.StartLine,
                                bp.Key.StartColumn,
                                bp.Key.EndLine,
                                bp.Key.EndColumn
                            })
                            .ToList();

                        string json = JsonConvert.SerializeObject(breakpointData, Formatting.Indented);
                        File.WriteAllText(breakPointsFile, json);
                    }
                    else
                    {
                        // Нет точек останова - удаляем файл, если он существует
                        if (File.Exists(breakPointsFile))
                        {
                            File.Delete(breakPointsFile);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка при сохранении точек останова: {ex}", (int)RpaCommon.Enums.LogLevel.Error);
            }
        }

        /// <summary>
        /// Save as the current state of a Workflow
        /// </summary>
        public void WorkflowSaveAs()
        {
            SaveFileDialog dialogSave = new SaveFileDialog
            {
                Title = "Save Workflow",
                Filter = "Workflows (.xaml)|*.xaml",
                FileName = "Workflow"
            };

            if (dialogSave.ShowDialog() == true)
            {
                _ = TrySaveDesigner(ActiveWorkflowDesignerEx, dialogSave.FileName);
            }
        }

        internal void RemoveDesignerByLoadedFile(string loadedFile)
        {
            WorkflowDesignerEx designer = GetDesignerByFile(loadedFile);

            if (designer != null)
            {
                // Фоновая валидация останавливается вместе с вкладкой: её таймер живёт на диспетчере
                // и после закрытия продолжает проверять невидимый дизайнер (а при остановке
                // приложения его тик пытается показать модальное окно конструктора).
                designer.DetachValidation();

                // Активный дизайнер фиксируется ДО закрытия вкладки: закрытие вкладки в UI поднимает
                // IsSelectedChanged и успевает переключить активного дизайнера на другую вкладку.
                // Чей выбор останется последним словом — решает панель документов (см. ниже).
                WorkflowDesignerEx activeBeforeClose = ActiveWorkflowDesignerEx;

                // Закрываем вкладку в UI перед удалением из коллекции
                DocGroupHelper docGroupHelper = DocGroupHelper.GetInstance();
                docGroupHelper?.CloseDocumentByPath(loadedFile);

                // Повторный вход через DocClosed → CmdRemoveDesigner (CloseDocumentByPath выше) вкладку
                // уже убрал: решение об активной вкладке принято, второй раз выбирать нельзя.
                if (!WorkflowDesigners.Contains(designer))
                {
                    return;
                }

                // Какая вкладка станет активной, решает панель документов — DocumentTabsCloseCoordinator
                // (правило DocumentTabPolicy): только она видит вкладки обоих видов, то есть то, что
                // реально было на экране. Модель лишь перестаёт считать активным закрытый дизайнер:
                // фоновую вкладку можно закрыть, не переключая видимую, а закрытый документ активным
                // остаться не может (иначе SyncWFControls и сохранение работали бы с закрытым документом).
                if (ReferenceEquals(activeBeforeClose, designer))
                {
                    ActiveWorkflowDesignerEx = null;
                }

                _ = WorkflowDesigners.Remove(designer);
            }
            else
            {
                Log($"Не удалось найти {loadedFile} для удаления", (int)RpaCommon.Enums.LogLevel.Warn);
            }
        }

        public CallResult SetActiveDesigner(string loadedFile)
        {
            WorkflowDesignerEx designer = GetDesignerByFile(loadedFile);
            if (designer != null)
            {
                ActiveWorkflowDesignerEx = GetDesignerByFile(loadedFile);
                return new CallResult();
            }
            else
            {
                string errorMessage = $"Не удалось найти вкладку {loadedFile} для удаления";
                Log(errorMessage, (int)RpaCommon.Enums.LogLevel.Warn);
                return new CallResult { Error = new Error(errorMessage) };
            }
        }

        #endregion Commands

        /// <summary>
        /// Get Designer By laoded File
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <returns></returns>
        public WorkflowDesignerEx GetDesignerByFile(string sourceFile)
        {
            return WorkflowDesigners
                .FirstOrDefault(d => ((WorkflowFileItem)d.Context.Items.First(i => i.ItemType == typeof(WorkflowFileItem))).LoadedFile == sourceFile);
        }

        /// <summary>
        /// Реестр вложенных XAML-активностей для открытия файла loose-путём (без сборки и без DLL).
        /// Возвращает <c>null</c> для файлов вне текущего WF-проекта и для не-WF проектов: тогда
        /// работает штатный путь дизайнера. Реестр строится один раз на проект и кэшируется до
        /// смены проекта (<see cref="LoadProject(Project)"/> сбрасывает кэш).
        /// </summary>
        internal WorkflowTypeRegistry GetLooseRegistryFor(string filePath)
        {
            if (Project == null
                || !Project.IsWorkflowProject
                || string.IsNullOrWhiteSpace(Project.ProjectDirPath))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(filePath))
            {
                string projectDir = Path.GetFullPath(Project.ProjectDirPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;

                string fullPath = Path.GetFullPath(filePath);

                if (!fullPath.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            if (_looseTypeRegistry == null)
            {
                _looseTypeRegistry = LooseXamlProjectService.BuildRegistry(Project);
            }

            return _looseTypeRegistry;
        }

        internal void LoadProject(string projectDirPath)
        {
            try
            {
                Project project = ObjectHelper.InitObject<Project>(Path.Combine(projectDirPath, Consts.projectFileName));
                if (project == null)
                {
                    project = new Project();
                }

                project.SetProjectDir(projectDirPath);
                LoadProject(project);
            }
            catch (Exception ex)
            {
                Log(ErrorText.Of(ex), (int)RpaCommon.Enums.LogLevel.Error);
            }
        }

        /// <summary>
        /// Загружает единый объект <see cref="Project"/> в дизайнер: заполняет <see cref="Project"/>
        /// и открывает MainActivity. Работает и для встроенного (project.json), и для Visual Studio
        /// (.csproj) формата.
        /// </summary>
        internal void LoadProject(Project project)
        {
            try
            {
                WorkflowDesigners.Clear();
                ActiveWorkflowDesignerEx = null;
                Project = project;
                _looseTypeRegistry = null;

                if (project == null || string.IsNullOrWhiteSpace(project.ProjectDirPath))
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(project.MainActivity))
                {
                    return;
                }

                string pathToMainActivity = Path.Combine(project.ProjectDirPath, project.MainActivity);

                // Остальные вкладки восстанавливает Main.RestoreSessionState: у него есть и вкладки
                // дизайнера, и вкладки редактора кода, и сохранённая активная вкладка.
                // Loose-реестр теперь берётся кэшированно через GetLooseRegistryFor (см. ниже).

                ActiveWorkflowDesignerEx = LoadActivityToDesignerHelper.LoadActivityToDesigner(pathToMainActivity, GetLooseRegistryFor(pathToMainActivity));
            }
            catch (Exception ex)
            {
                Log(ErrorText.Of(ex), (int)RpaCommon.Enums.LogLevel.Error);
            }
        }
    }

    public enum CustomWfDesignerState
    {
        Stop,
        Run,
        Pause,
        Breakpoint
    }
}
