using FW.EasyMvvm.Core;
using RPA.Models;

namespace RPA.ViewModels
{
    internal class VsTemplateProjectCreatorViewModel : BaseViewModel
    {
        private VsTemplateProjectCreator creator;

        public override void InitializeViewModel()
        {
            creator = (VsTemplateProjectCreator)Model;
        }

        #region Properties

        public string ProjectName
        {
            get => creator.ProjectName;
            set => creator.ProjectName = value;
        }

        public string SelectedPath
        {
            get => creator.SelectedPath;
            set => creator.SelectedPath = value;
        }

        public string TargetFrameworkVersion
        {
            get => creator.TargetFrameworkVersion;
            set => creator.TargetFrameworkVersion = value;
        }

        public string[] TargetFrameworkVersions => creator.TargetFrameworkVersions;

        #endregion Properties
    }
}
