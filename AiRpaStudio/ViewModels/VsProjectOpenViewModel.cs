using FW.EasyMvvm.Core;
using RPA.Models;

namespace RPA.ViewModels
{
    internal class VsProjectOpenViewModel : BaseViewModel
    {
        private VsProjectOpen model;

        public override void InitializeViewModel()
        {
            model = (VsProjectOpen)Model;
        }

        public string FilePath
        {
            get => model.FilePath;
            set => model.FilePath = value;
        }
    }
}
