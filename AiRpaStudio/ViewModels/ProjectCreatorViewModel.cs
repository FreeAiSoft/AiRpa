using FW.EasyMvvm.Core;
using RPA.Entities;
using RPA.Models;

namespace RPA.ViewModels
{
    internal class ProjectCreatorViewModel : BaseViewModel
    {
        private ProjectCreator projectCreator;

        public override void InitializeViewModel()
        {
            projectCreator = (ProjectCreator)Model;
        }

        #region Properties

        public Project Project => projectCreator.Project;

        public string SelectedPath
        {
            get => projectCreator.SelectedPath;
            set => projectCreator.SelectedPath = value;
        }

        #endregion Properties
    }
}
