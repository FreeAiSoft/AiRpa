using System.Collections.Generic;
using System.Collections.ObjectModel;
using FW.EasyMvvm.Core;
using RPA.Core;
using RPA.Entities;

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

        public string AdditionalColor => Services.AppSettings?.AdditionalColor;

        public ObservableCollection<WorkflowDesignerEx> WorkflowDesigners => main.CustomWfDesigner.WorkflowDesigners;

        public WorkflowDesignerEx ActiveWorkflowDesigner => main.CustomWfDesigner.ActiveWorkflowDesignerEx;

        public ObservableCollection<FSNode> FSNodes => main.ProjectManager.FSNodes;

        public CustomWfDesigner CustomWfDesignerState => main.CustomWfDesigner;

        public string Language
        {
            get => main.Language;
            set => main.Language = value;
        }

        public List<string> Langs => new List<string> { "RU", "EN" };

        public bool CanSave => main.CustomWfDesigner.CanSave;

        public bool CanSaveAll => main.CustomWfDesigner.CanSaveAll;

        #endregion Properties
    }
}
