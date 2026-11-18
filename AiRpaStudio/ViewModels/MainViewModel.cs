using System.Collections.Generic;
using System.Collections.ObjectModel;
using Agent.Mvvm;
using RPA.Core;
using RPA.Entities;
using RPA.Models;

namespace RPA.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private Models.Main main;

        public override void InitializeViewModel()
        {
            main = (Models.Main)Model;
        }

        #region Properties

        public ObservableCollection<WorkflowDesignerEx> WorkflowDesigners => main.CustomWfDesigner.WorkflowDesigners;

        public WorkflowDesignerEx ActiveWorkflowDesigner => main.CustomWfDesigner.ActiveWorkflowDesignerEx;

        public ObservableCollection<FSNode> FSNodes => main.ProjectManager.FSNodes;

        public ObservableCollection<CodeDocument> OpenCodeDocuments => main.OpenCodeDocuments;

        public ObservableCollection<RecentProjectEntry> RecentProjects => main.RecentProjects;

        public CustomWfDesigner CustomWfDesignerState => main.CustomWfDesigner;

        public string Language
        {
            get => main.Language;
            set => main.Language = value;
        }

        public List<string> Langs => new List<string> { "RU", "EN" };

        public bool CanSave => main.CanSave;

        public bool CanSaveAll => main.CanSaveAll;

        #endregion Properties

        /// <summary>Открывает недавний проект по его пути (вызывается из меню «Файл → Недавние проекты»).</summary>
        public void OpenRecentProject(string path) => main.CmdOpenRecentProject(path);
    }
}
