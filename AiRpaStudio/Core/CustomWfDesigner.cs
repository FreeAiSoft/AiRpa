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
                DocGroupHelper.GetInstance().SetSelectedTab(_activeWorkflowDesigner);
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

        private void WorkflowDesigners_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            try
            {
                string files = JsonConvert.SerializeObject(WorkflowDesigners.Select(wd => wd.Context.Items.GetValue<WorkflowFileItem>().LoadedFile).ToList());
                Properties.Settings.Default.DesignerFiles = files;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
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
                using StreamReader file = new StreamReader(dialogOpen.FileName, true);
                ActiveWorkflowDesignerEx = LoadActivityToDesignerHelper.LoadActivityToDesigner(dialogOpen.FileName);
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
                    ActiveWorkflowDesignerEx.Save(dialogSave.FileName);
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
                ActiveWorkflowDesignerEx.Save(loadedFile);

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
                    designer.Save(loadedFile);
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
                Log($"Ошибка при сохранении точек останова: {ex.Message}", (int)RpaCommon.Enums.LogLevel.Error);
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
                ActiveWorkflowDesignerEx.Save(dialogSave.FileName);
            }
        }

        internal void RemoveDesignerByLoadedFile(string loadedFile)
        {
            WorkflowDesignerEx designer = GetDesignerByFile(loadedFile);

            if (designer != null)
            {
                // Закрываем вкладку в UI перед удалением из коллекции
                DocGroupHelper docGroupHelper = DocGroupHelper.GetInstance();
                docGroupHelper?.CloseDocumentByPath(loadedFile);

                ActiveWorkflowDesignerEx = WorkflowDesigners.Count > 0 ? WorkflowDesigners[0] : null;
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

        internal void LoadProject(string projectDirPath)
        {
            try
            {
                WorkflowDesigners.CollectionChanged -= WorkflowDesigners_CollectionChanged;
                WorkflowDesigners.Clear();
                ActiveWorkflowDesignerEx = null;
                Project = ObjectHelper.InitObject<Project>(Path.Combine(projectDirPath, Consts.projectFileName));
                Project.SetProjectDir(projectDirPath);
                string pathToMainActivity = Path.Combine(projectDirPath, Project.MainActivity);
                RestoreOtherDesigners(pathToMainActivity);
                ActiveWorkflowDesignerEx = LoadActivityToDesignerHelper.LoadActivityToDesigner(pathToMainActivity);
                WorkflowDesigners.CollectionChanged += WorkflowDesigners_CollectionChanged;
            }
            catch (Exception ex)
            {
                Log(ex.Message, (int)RpaCommon.Enums.LogLevel.Error);
            }
        }

        /// <summary>
        /// Загрузка активностей в диайнер
        /// </summary>
        /// <param name="pathToMainActivity"></param>
        private void RestoreOtherDesigners(string pathToMainActivity)
        {
            try
            {
                if (!string.IsNullOrEmpty(Properties.Settings.Default.DesignerFiles))
                {
                    List<string> paths = JsonConvert.DeserializeObject<List<string>>(Properties.Settings.Default.DesignerFiles);
                    _ = paths.Remove(pathToMainActivity);
                    paths.ForEach(f => LoadActivityToDesignerHelper.LoadActivityToDesigner(f));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
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
