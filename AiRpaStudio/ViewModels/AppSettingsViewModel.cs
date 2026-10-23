using System.Collections.Generic;
using FW.EasyMvvm.Core;
using RPA.Models;

namespace RPA.ViewModels
{
    internal class AppSettingsViewModel : BaseViewModel
    {
        private AppSettings appSettings;

        public override void InitializeViewModel()
        {
            appSettings = (AppSettings)Model;
        }

        #region Properties

        public List<string> LightOrDarkList => new List<string>() { "Light", "Dark" };

        public string LightOrDark
        {
            get => appSettings.LightOrDark;
            set => appSettings.LightOrDark = value;
        }

        public List<string> Themes => appSettings.Themes;

        public string SelectedTheme
        {
            get => appSettings.SelectedTheme;
            set => appSettings.SelectedTheme = value;
        }

        #endregion Properties
    }
}
