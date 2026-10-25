using System;
using System.Activities.Core.Presentation;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using FW.EasyMvvm.Core;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using RPA.Core;
using RPA.Enums;
using RPA.Helpers;
using RPA.ViewModels;
using RPA.Views;
using RPA.VsProject;
using RpaCommon;

namespace RPA.Models
{
    public class Main : BaseModel
    {
        #region MVVM

        #region Fields

        private string language;

        #endregion Fields

        #region Properties
        public CustomWfDesigner CustomWfDesigner { get; } = CustomWfDesigner.GetInstance();

        public ProjectManager ProjectManager { get; set; } = ProjectManager.GetInstance();

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
            Services.AppSettings.SetTheme();
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
            if (string.IsNullOrWhiteSpace(Properties.Settings.Default.LastLoadedProjectPath))
            {
                CreateHelloWorldProject();
            }
            LoadProject(Properties.Settings.Default.LastLoadedProjectPath);
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
            try
            {
                CallResult resp = ProjectManager.InitFileTree(rootProjectFolder);
                if (!resp.Success)
                {
                    OnSendMessage(resp.Error.Message);
                    return;
                }

                CustomWfDesigner.LoadProject(rootProjectFolder);
                Properties.Settings.Default.LastLoadedProjectPath = rootProjectFolder;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                CustomWfDesigner.GetInstance().Log($"Ошибка загрузки проекта: {ex.Message}", (int)RpaCommon.Enums.LogLevel.Error);
            }
        }

        #region Commands

        #region Files

        /// <summary>
        /// Add New Activity To Project
        /// </summary>
        public void CmdAddNewActivityToProject()
        {
            ActivityCreator activityCreator = new ActivityCreator();
            MvvmFactory.CreateWindow(activityCreator, new ActivityCreatorViewModel(), new ActivityCreatorView(), ViewMode.ShowDialog);
            if (activityCreator.IsCreate)
            {
                CustomWfDesigner.ActiveWorkflowDesignerEx = LoadActivityToDesignerHelper.LoadActivityToDesigner(activityCreator.FilePath);
                ProjectManager.AddFile(activityCreator.FilePath);
            }
        }

        /// <summary>
        /// Add New Activity To Project
        /// </summary>
        public void CmdAddNewActivityToProjectFolder(string folderPath)
        {
            ActivityCreator activityCreator = new ActivityCreator(folderPath);
            MvvmFactory.CreateWindow(activityCreator, new ActivityCreatorViewModel(), new ActivityCreatorView(), ViewMode.ShowDialog);
            if (activityCreator.IsCreate)
            {
                CustomWfDesigner.ActiveWorkflowDesignerEx = LoadActivityToDesignerHelper.LoadActivityToDesigner(activityCreator.FilePath);
                ProjectManager.AddFile(activityCreator.FilePath);
            }
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
                    CustomWfDesigner.ActiveWorkflowDesignerEx = LoadActivityToDesignerHelper.LoadActivityToDesigner(filePath);
                }
                catch (Exception ex)
                {
                    CustomWfDesigner.Log(LocalizeHelper.Get("lang_msg_xaml_load_failed", ex.Message), (int)RpaCommon.Enums.LogLevel.Error);
                    OnSendMessage(LocalizeHelper.Get("lang_msg_xaml_load_failed", ex.Message));
                }
            }
            else if (filePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                     || filePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                CmdVsOpenInVisualStudio(filePath);
            }
            else
            {
                try
                {
                    Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    OnSendMessage(LocalizeHelper.Get("lang_msg_open_file_error", ex.Message));
                }
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
        /// App settings
        /// </summary>
        public void Settings()
        {
            MvvmFactory.CreateWindow(AppSettings.GetInstance(), new AppSettingsViewModel(), new AppSettingsView(), ViewMode.ShowDialog);
        }

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
                CustomWfDesigner.Log("Ошибка открытия в Visual Studio: " + ex.Message, (int)RpaCommon.Enums.LogLevel.Error);
                OnSendMessage(LocalizeHelper.Get("lang_msg_open_vs_error", ex.Message));
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
            if (string.IsNullOrWhiteSpace(path))
            {
                OnSendMessage(LocalizeHelper.Get("lang_msg_specify_path"));
                return;
            }

            CustomWfDesigner.Log(LocalizeHelper.Get("lang_msg_build_started", path), (int)RpaCommon.Enums.LogLevel.Info);

            VsBuildResult result = VsProjectBuilder.Build(
                path,
                "Debug",
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
            try
            {
                CallResult resp = ProjectManager.InitVsProjectTree(path);
                if (!resp.Success)
                {
                    OnSendMessage(resp.Error.Message);
                    return;
                }

                CustomWfDesigner.Log(LocalizeHelper.Get("lang_msg_vs_project_loaded", path), (int)RpaCommon.Enums.LogLevel.Info);
            }
            catch (Exception ex)
            {
                CustomWfDesigner.Log(LocalizeHelper.Get("lang_msg_vs_project_load_error", ex.Message), (int)RpaCommon.Enums.LogLevel.Error);
                OnSendMessage(LocalizeHelper.Get("lang_msg_vs_project_load_error", ex.Message));
            }
        }

        private async void OfferVsProjectActions(VsTemplateProjectCreator creator)
        {
            if (!(ViewModel?.View is MetroWindow window))
            {
                return;
            }

            MessageDialogResult choice = await window.ShowMessageAsync(
                LocalizeHelper.Get("lang_vs_project_created"),
                creator.ProjectFilePath,
                MessageDialogStyle.AffirmativeAndNegativeAndDoubleAuxiliary,
                new MetroDialogSettings
                {
                    AffirmativeButtonText = LocalizeHelper.Get("lang_open_in_vs"),
                    NegativeButtonText = LocalizeHelper.Get("lang_close"),
                    FirstAuxiliaryButtonText = LocalizeHelper.Get("lang_load_into_studio"),
                    SecondAuxiliaryButtonText = LocalizeHelper.Get("lang_build")
                });

            switch (choice)
            {
                case MessageDialogResult.Affirmative:
                    CmdVsOpenInVisualStudio(creator.ProjectFilePath);
                    break;
                case MessageDialogResult.FirstAuxiliary:
                    LoadVsProjectIntoStudio(creator.ProjectFilePath);
                    break;
                case MessageDialogResult.SecondAuxiliary:
                    CmdVsBuild(creator.ProjectFilePath);
                    break;
            }
        }

        #endregion Visual Studio

        #endregion Commands
    }
}
