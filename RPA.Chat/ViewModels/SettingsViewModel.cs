using System;
using System.Windows.Input;
using RPA.Chat.Services;

namespace RPA.Chat.ViewModels
{
    /// <summary>
    /// View model for the chat Settings tab. Wraps the same <see cref="SettingsData"/>
    /// instance owned by <see cref="ChatViewModel"/>, so edits flow straight into the
    /// persisted model; <see cref="Save"/> persists and triggers a provider/tools rebuild.
    /// </summary>
    public sealed class SettingsViewModel
    {
        private readonly Action _onSaved;

        public SettingsViewModel(SettingsData settings, Action onSaved)
        {
            Data = settings ?? new SettingsData();
            _onSaved = onSaved;
            SaveCommand = new RelayCommand(Save);
        }

        public SettingsData Data { get; }

        public ICommand SaveCommand { get; }

        public string[] SpeechToTextProviderOptions { get; } = { "Auto", "OpenAiWhisper", "Vosk" };

        public string[] ToolCallingModeOptions { get; } = { "JsonChat", "Native" };

        public string[] MemoryBankModeOptions { get; } = { "Disabled", "Tool", "Convention" };

        /// <summary>Index of the active settings section (0..4: API, General, Sound, Timeouts, Proxy).</summary>
        public int SettingsSectionIndex { get; set; }

        private void Save()
        {
            _onSaved?.Invoke();
        }
    }
}
