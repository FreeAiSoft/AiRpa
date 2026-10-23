using FW.EasyMvvm.Core;
using RPA.Models;

namespace RPA.ViewModels
{
    internal class ActivityCreatorViewModel : BaseViewModel
    {
        private ActivityCreator activityCreator;

        public override void InitializeViewModel()
        {
            activityCreator = (ActivityCreator)Model;
        }

        #region Properties

        public string SelectedPath
        {
            get => activityCreator.SelectedPath;
            set => activityCreator.SelectedPath = value;
        }

        public string ActivityName
        {
            get => activityCreator.ActivityName;
            set => activityCreator.ActivityName = value;
        }

        #endregion Properties
    }
}
