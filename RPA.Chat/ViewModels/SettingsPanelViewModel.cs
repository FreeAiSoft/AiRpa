using Agent.Models;
using Agent.Mvvm;

namespace RPA.Chat.ViewModels
{
    /// <summary>
    /// Минимальная VM для вкладки «Settings» тулкита: выставляет <see cref="SettingsModel"/> и
    /// наследует <c>Cmd</c> из <see cref="BaseViewModel"/>, который пробрасывает команды
    /// (например, <c>SaveSettingsCommand</c>) в модель через <see cref="BaseViewModel.SetModel"/>.
    /// </summary>
    public sealed class SettingsPanelViewModel : BaseViewModel
    {
        public SettingsPanelViewModel(SettingsModel settings)
        {
            SetModel(settings, null);
        }

        public SettingsModel Settings => (SettingsModel)Model;
    }
}