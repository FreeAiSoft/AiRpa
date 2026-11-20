using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Agent.Core.Contracts;
using Agent.Core.Models;
using Agent.Toolkit.Wpf.Controls;
using Agent.Toolkit.Wpf.Markdown;
using Agent.Models;
using RPA.Shared.Models;
using RPA.Chat.Services;
using RPA.Chat.ViewModels;

namespace RPA.Chat.Views
{
    /// <summary>
    /// Тонкая обёртка над <see cref="AgentTabControl"/>: собирает связку
    /// <c>ChatModel + StudioChatHost + ChatControl</c> и подключает UI-слоты (ввод текста,
    /// документация, вопросы, автоскролл). Студия кладёт в док именно этот контрол.
    /// </summary>
    public partial class ChatHostView : UserControl, INotifyPropertyChanged
    {
        private readonly AgentTabControl _mainTabs;
        private readonly ChatControl _chatControl;
        private readonly StudioChatHost _host;
        private readonly ChatModel _model;
        private readonly ChatSettingsStore _settingsStore;
        private readonly ChatSecretsStore _secretsStore;
        private readonly Agent.Core.Services.SettingsData _settings;
        private readonly SettingsModel _settingsModel;

        public ChatHostView()
        {
            InitializeComponent();

            _settingsStore = new ChatSettingsStore();
            _secretsStore = new ChatSecretsStore();
            _settings = _settingsStore.Load();

            // Студия владеет темой (режим Auto): SettingsControl не должен переключать её сам.
            _settings.ThemeMode = "Auto";

            // API-ключ живёт в secrets store; на случай старого файла настроек ключ мигрирует
            // из chat_settings.json при первом чтении (SettingsModel.Load делает то же самое).
            var apiKey = _secretsStore.LoadApiKey();
            if (!string.IsNullOrEmpty(apiKey))
                _settings.ApiKey = apiKey;

            _model = new ChatModel(new ChatHistoryStore(), new UiStateStore(), chatSink: new HostPromptSink(this), logger: ChatLogLogger.Instance);
            _host = new StudioChatHost(_model, _settings, _settingsStore);

            _mainTabs = new AgentTabControl();

            // Вкладка «Chat» — тот же ChatControl, что и раньше, но теперь внутри AgentTabControl.
            _chatControl = _mainTabs.ChatPanel;
            _chatControl.Margin = new Thickness(5, 0, 5, 5);
            _chatControl.DataContext = _model;
            _chatControl.Host = _host;
            _chatControl.Settings = _settings;
            _chatControl.SettingsPersistence = _settingsStore;

            // Голосовой ввод (IMicrophoneRecorder / ISpeechToTextService) поставляет внешний проект;
            // в RPA.Chat этот контракт не реализуется (см. решение S3 плана интеграции).
            // _chatControl.AttachServices(/* внешний mic */, /* внешний stt */);

            // Вкладка «Settings» — минимальная VM (SettingsModel + Cmd).
            _settingsModel = new SettingsModel(_settingsStore, secretsStore: _secretsStore);
            _settingsModel.Run();
            _settingsModel.ThemeMode = "Auto";
            _mainTabs.SettingsPanel.DataContext = new SettingsPanelViewModel(_settingsModel);
            WireApiKeyBox();

            _host.InputWriter = text => _chatControl.SetInputText(text);
            _host.DocsRenderer = RenderDocs;

            _chatControl.MessagesList.QuestionAnswered += OnQuestionAnswered;
            _chatControl.MessagesList.QuestionSkipped += OnQuestionSkipped;
            _model.Messages.CollectionChanged += OnMessagesCollectionChanged;

            Root.Children.Add(_mainTabs);

            _model.Run();
            RestoreUiState();
        }

        private void WireApiKeyBox()
        {
            var box = _mainTabs.SettingsPanel.ApiKeyBox;
            if (box == null)
                return;

            box.Password = _settingsModel.ApiKey ?? string.Empty;
            box.PasswordChanged += (s, e) => _settingsModel.ApiKey = box.Password;
        }

        public StudioChatHost Host => _host;

        public ChatControl ChatControl => _chatControl;

        /// <summary>
        /// Тема продукта (единственный источник истины — канонические настройки
        /// <see cref="Agent.Core.Services.SettingsData.BaseTheme"/>).
        /// </summary>
        public StudioTheme Theme
        {
            get => StudioThemes.Parse(_settings?.BaseTheme, StudioTheme.Dark);
            set
            {
                if (_settings == null || Theme == value)
                    return;

                _settings.BaseTheme = value.ToName();

                // Сначала хост студии пересобирает палитру Studio.* и транслирует её в Theme.*
                // (AgentThemeManager.Apply); ChatControl читает Theme.* через DynamicResource и
                // перекрашивается сам, без ChatStudioBridge.
                ThemeApplying?.Invoke(value);
                OnPropertyChanged(nameof(Theme));
                OnPropertyChanged(nameof(IsDarkTheme));
                _settingsStore.Save(_settings);
                ThemeChanged?.Invoke();
            }
        }

        /// <summary>Проекция <see cref="Theme"/> для двухпозиционного тумблера в заголовке студии.</summary>
        public bool IsDarkTheme
        {
            get => Theme == StudioTheme.Dark;
            set => Theme = value ? StudioTheme.Dark : StudioTheme.Light;
        }

        /// <summary>Вызывается перед сменой темы: хост студии применяет Studio.* + Theme.*.</summary>
        public Action<StudioTheme> ThemeApplying { get; set; }

        /// <summary>Вызывается после сохранения темы.</summary>
        public event Action ThemeChanged;

        /// <summary>Отправляет готовый промпт в чат (используется студией для «отправить ошибку дизайнера»).</summary>
        public async Task SendPromptAsync(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return;

            _chatControl.EnsureChatReadyToReceive();
            await _host.SendChatAsync(prompt, Array.Empty<ChatAttachment>());
        }

        private void RenderDocs(string title, string markdown)
        {
            _chatControl.DocsTitleTextControl.Text = string.IsNullOrWhiteSpace(title) ? "Documentation" : title;
            _chatControl.DocsContentControl.Content = MarkdownRenderer.Render(markdown);
            MarkdownRenderer.RelativeLinkNavigator = _host.NavigateDocs;
        }

        private void OnQuestionAnswered(object sender, ChatMessage message)
        {
            _host.AnswerQuestion(message);
        }

        private void OnQuestionSkipped(object sender, ChatMessage message)
        {
            _host.SkipQuestion(message);
        }

        private void OnMessagesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Сообщения хост пишет прямо в ChatModel; автоскролл ChatControl срабатывает только
            // при его собственном Append, поэтому доскролливаем после добавления.
            Dispatcher.BeginInvoke(new Action(() => _chatControl.MessagesList.ScrollToEnd()),
                DispatcherPriority.Loaded);
        }

        private void RestoreUiState()
        {
            _chatControl.RestoringUiState = true;
            try
            {
                var state = _model.LoadUiState();
                _model.RestoreMode(state.AgentMode);

                if (state.ChatViewMode == ChatViewMode.History)
                {
                    _chatControl.ShowHistoryView();
                }
                else
                {
                    var toOpen = _model.ResolveConversationToOpen(state.LastConversationId);
                    if (toOpen != null)
                        _model.OpenConversation(toOpen.Id, markDirty: false);
                    else
                        _model.StartNewChat(markDirty: false);

                    _chatControl.ShowConversationView();
                }
            }
            finally
            {
                _chatControl.RestoringUiState = false;
            }
        }

        private sealed class HostPromptSink : IChatPromptSink
        {
            private readonly ChatHostView _owner;

            public HostPromptSink(ChatHostView owner)
            {
                _owner = owner;
            }

            public Task SendPromptAsync(string prompt, CancellationToken cancellationToken)
            {
                return _owner._host.SendChatAsync(prompt, Array.Empty<ChatAttachment>());
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
