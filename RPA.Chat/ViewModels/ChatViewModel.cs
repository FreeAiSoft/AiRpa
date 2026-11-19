using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using RPA.Shared.AgentTools;
using Agent.Core.Context;
using Agent.Core.Contracts;
using RPA.Shared.Services.Documents;
using Agent.Core.Models;
using RPA.Shared.Models;
using Agent.Core.Services;
using Agent.Models;
using RPA.Shared.Services;
using RPA.Chat.Controls;
using Agent.Toolkit.Wpf.Markdown;
using RPA.Chat.Services;
using RPA.Chat.Services.AgentTools;

namespace RPA.Chat.ViewModels
{
    /// <summary>
    /// Classic-MVVM view model for the studio chat: owns the agent loop, streaming,
    /// Plan/Act/Help mode, conversation history, attachments, questions and token usage.
    /// UI-thread marshalling goes through the captured <see cref="SynchronizationContext"/>
    /// so the VM remains unit-testable without a WPF dispatcher.
    /// </summary>
    public sealed class ChatViewModel : INotifyPropertyChanged
    {
        private const string SkipAnswerText = "User skipped the question.";

        private readonly SynchronizationContext _uiContext;

        private RPA.Chat.Services.SettingsData _settings;
        private SettingsViewModel _settingsViewModel;
        private IAiProvider _ai;
        private IBuildFeedbackService _buildFeedback;
        private readonly GitService _git = new GitService();
        private readonly ChatModel _chat;
        private readonly ContextUsageBinder _contextUsageBinder;
        private bool _isSending;
        private bool _isRecording;
        private bool _isTranscribing;
        private AgentToolRegistry _toolRegistry;
        private AgentRunner _agentRunner;
        private MicrophoneRecorder _mic;
        private ISpeechToTextService _stt;
        private CancellationTokenSource _chatCts;
        private TaskCompletionSource<string> _questionTcs;
        private ChatMessage _activeQuestion;
        private bool _restoringUiState = true;

        // ---- Commit workflow ----
        private const string CommitBranchName = "dev";
        private const int CommitDiffMaxChars = 8000;
        private const int ErrorContextLines = 15;
        private const int MaxErrorsWithContext = 10;
        private const string CommitMessageSystemPrompt =
            "You are an expert at writing concise git commit messages. " +
            "Summarize the provided changes into a single-line commit message in the format \"type(scope): summary\". " +
            "Write the summary in Russian. " +
            "Respond with only the commit message text — no markdown, no quotes, no extra explanation.";
        private const string CommitOtherSummarySystemPrompt =
            "You are an expert at writing concise git commit messages. " +
            "Summarize the changes below into a single concise line in the imperative mood. " +
            "Write the summary in Russian. " +
            "Respond with only the summary text — no markdown, no quotes, no extra explanation.";

        private bool _isCommitOpen;
        private bool _isCommitEditorVisible;
        private bool _isCommitBusy;
        private string _commitMessage = "";
        private string _commitStatusText = "";
        private string _commitWarningMessage = "";
        private bool _hasCommitWarning;
        private bool _hasCommitResult;
        private string _commitResultMessage = "";
        private NotificationKind _commitResultKind = NotificationKind.Success;

        private bool _isPermissionsOpen;
        private bool _isDocsOpen;
        private string _docsContent = "";
        private string _docsCurrentPath = DocumentationViewer.IndexFileName;
        private readonly DocumentationViewer _docsViewer = new DocumentationViewer();

        private readonly ObservableCollection<SegmentedToggleItem> _modeItems = new ObservableCollection<SegmentedToggleItem>();

        private string _inputText = "";
        private string _statusText = "";
        private string _noticeMessage = "";
        private bool _hasNotice;

        public ChatViewModel()
        {
            _uiContext = SynchronizationContext.Current;
            _chat = new ChatModel(new ChatHistoryStore(), new UiStateStore());
            _contextUsageBinder = new ContextUsageBinder(_chat, _uiContext);

            var messagesView = (ListCollectionView)CollectionViewSource.GetDefaultView(_chat.Messages);
            messagesView.Filter = m => !ReferenceEquals(m, _chat.Messages.FirstOrDefault());
            _chat.Messages.CollectionChanged += OnMessagesCollectionChanged;

            _modeItems.Add(new SegmentedToggleItem { Label = "Plan", Value = "Plan", Accent = ModeBrush(AccentHex(AgentMode.Plan)), ToolTip = "Plan mode" });
            _modeItems.Add(new SegmentedToggleItem { Label = "Act", Value = "Act", Accent = ModeBrush(AccentHex(AgentMode.Act)), ToolTip = "Act mode" });
            _modeItems.Add(new SegmentedToggleItem { Label = "Help", Value = "Help", Accent = ModeBrush(AccentHex(AgentMode.Help)), ToolTip = "Help mode" });

            SendCommand = new AsyncRelayCommand(SendAsync, () => !IsSending);
            StopCommand = new RelayCommand(Stop, () => IsSending);
            NewChatCommand = new RelayCommand(() => StartNewChat());
            ShowHistoryCommand = new RelayCommand(ShowHistoryView);
            BackToChatCommand = new RelayCommand(ShowConversationView);
            OpenConversationCommand = new RelayCommand<ChatConversation>(c => OpenConversation(c?.Id));
            DeleteConversationCommand = new RelayCommand<ChatConversation>(c => DeleteConversation(c?.Id));
            ClearHistoryCommand = new RelayCommand(ClearHistory);
            AttachFilesCommand = new RelayCommand(AttachFiles);
            RemoveAttachmentCommand = new RelayCommand<ChatAttachment>(RemoveAttachment);
            CopyMessageCommand = new RelayCommand<ChatMessage>(CopyMessage);
            AnswerQuestionCommand = new RelayCommand<string>(AnswerQuestion);
            SkipQuestionCommand = new RelayCommand(SkipQuestion);
            ToggleRecordingCommand = new AsyncRelayCommand(ToggleRecordingAsync);
            CompactNowCommand = new AsyncRelayCommand(CompactNowAsync);
            CommitCommand = new AsyncRelayCommand(CommitAsync);
            CommitSendCommand = new AsyncRelayCommand(CommitSendAsync);
            CommitAutoGenerateCommand = new AsyncRelayCommand(CommitAutoGenerateAsync);
            CommitCloseCommand = new RelayCommand(CloseCommitControl);
            CommitResultCloseCommand = new RelayCommand(HideCommitResult);
            CopyConversationCommand = new RelayCommand(CopyConversation);
            ClearInputCommand = new RelayCommand(ClearInput);
            DoItCommand = new AsyncRelayCommand(DoItAsync);
            SendErrorsCommand = new AsyncRelayCommand(SendErrorsAsync);
            UpdateMemoryCommand = new RelayCommand(UpdateMemory);
            PermissionsCommand = new RelayCommand(OpenPermissions);
            PermissionsCloseCommand = new RelayCommand(ClosePermissions);
            DocsCommand = new RelayCommand(OpenDocs);
            DocsHomeCommand = new RelayCommand(ShowDocsIndex);
            DocsCloseCommand = new RelayCommand(CloseDocs);
            RefreshPromptsCommand = new RelayCommand(RefreshPrompts);

            _settings = RPA.Chat.Services.SettingsStore.Load();
            _settingsViewModel = new SettingsViewModel(_settings, ApplySettings);
            _buildFeedback = new MsBuildFeedbackService(GetSolutionDirectory);
            RecreateAiProvider();
            _mic = new MicrophoneRecorder();
            _mic.LevelChanged += OnMicLevelChanged;
            _stt = SpeechToTextFactory.Create(_settings);
            RebuildAgentTools();

            _chat.Run();
            RestoreUiState();
        }

        // ------------------------------------------------------------------
        // Collections exposed to the view
        // ------------------------------------------------------------------

        public ObservableCollection<ChatMessage> Messages => _chat.Messages;

        public ObservableCollection<ChatAttachment> Attachments => _chat.Attachments;

        public ObservableCollection<ChatConversation> Conversations => _chat.History;

        public ObservableCollection<SegmentedToggleItem> ModeItems => _modeItems;

        // ------------------------------------------------------------------
        // Bindable state
        // ------------------------------------------------------------------

        public string InputText
        {
            get => _inputText;
            set
            {
                if (_inputText != value)
                {
                    _inputText = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanSend));
                }
            }
        }

        public bool IsSending
        {
            get => _isSending;
            private set
            {
                if (_isSending != value)
                {
                    _isSending = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanSend));
                    SendCommand.RaiseCanExecuteChanged();
                    StopCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsRecording
        {
            get => _isRecording;
            private set
            {
                if (_isRecording != value)
                {
                    _isRecording = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MicToolTip));
                }
            }
        }

        public bool IsTranscribing
        {
            get => _isTranscribing;
            private set
            {
                if (_isTranscribing != value)
                {
                    _isTranscribing = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MicToolTip));
                }
            }
        }

        public string MicToolTip => IsTranscribing ? "Transcribing…" : (IsRecording ? "Stop recording" : "Start voice input");

        public string ModeName
        {
            get => _chat.Mode == AgentMode.Act ? "Act" : (_chat.Mode == AgentMode.Help ? "Help" : "Plan");
            set => ApplyModeName(value);
        }

        public AgentMode Mode => _chat.Mode;

        /// <summary>Accent brush of the currently selected mode (Plan/Act/Help), used to tint the composer input border.</summary>
        public Brush ModeAccentBrush => ModeBrush(AccentHex(_chat.Mode));

        public bool CanSend => !IsSending;

        public string StatusText
        {
            get => _statusText;
            private set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
        }

        /// <summary>The shared chat model, bound by the view's context indicator.</summary>
        public ChatModel Chat => _chat;

        /// <summary>Text of the pinned first message (with role label), or empty when the chat is empty.</summary>
        public string PinnedMessageText => _chat.PinnedMessage?.DisplayText ?? "";

        /// <summary>True when the pinned first message should be rendered: conversation view is active and there is at least one message.</summary>
        public bool IsPinnedMessageVisible => _chat.IsConversationView && _chat.HasPinnedMessage;

        /// <summary>Messages rendered in the scroll list — everything except the pinned first message.</summary>
        public ICollectionView ChatMessages => CollectionViewSource.GetDefaultView(_chat.Messages);

        public bool IsConversationView => _chat.IsConversationView;

        public bool HasAttachments => _chat.Attachments.Count > 0;

        public bool HasNotice
        {
            get => _hasNotice;
            private set { if (_hasNotice != value) { _hasNotice = value; OnPropertyChanged(); } }
        }

        public string NoticeMessage
        {
            get => _noticeMessage;
            private set { if (_noticeMessage != value) { _noticeMessage = value; OnPropertyChanged(); } }
        }

        public NotificationKind NoticeKind { get; private set; } = NotificationKind.Info;

        public RPA.Chat.Services.SettingsData Settings => _settings;

        public SettingsViewModel SettingsViewModel => _settingsViewModel;

        /// <summary>
        /// Тема продукта — перечисление <see cref="StudioTheme"/> (единственный источник истины, см.
        /// <c>RPA.Helpers.StudioPalette.Current</c>). Персистится строкой <c>"Dark"</c>/<c>"Light"</c>
        /// (<see cref="RPA.Chat.Services.SettingsData.Theme"/>), а не <c>bool</c>.
        /// </summary>
        public StudioTheme Theme
        {
            get => StudioThemes.Parse(_settings?.Theme, StudioTheme.Dark);
            set
            {
                if (_settings == null)
                    return;
                if (Theme == value)
                    return;

                _settings.Theme = value.ToName();

                // Порядок обязателен: сначала хост пересобирает палитру Studio.*, и только потом
                // контрол перекрывает свой словарь мостом (ChatStudioBridge.Apply). Иначе мост
                // прочитает кисти предыдущей темы, и чат отстанет на один переключатель.
                ThemeApplying?.Invoke(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDarkTheme));
                RPA.Chat.Services.SettingsStore.Save(_settings);
                ThemeChanged?.Invoke();
            }
        }

        /// <summary>
        /// Проекция <see cref="Theme"/> для двухпозиционного переключателя в заголовке студии:
        /// сам тумблер умеет только «вкл/выкл», поэтому <c>true</c> = <see cref="StudioTheme.Dark"/>.
        /// Источник истины всё равно <see cref="Theme"/>.
        /// </summary>
        public bool IsDarkTheme
        {
            get => Theme == StudioTheme.Dark;
            set => Theme = value ? StudioTheme.Dark : StudioTheme.Light;
        }

        // ------------------------------------------------------------------
        // Commit / overlays state
        // ------------------------------------------------------------------

        public bool IsCommitOpen
        {
            get => _isCommitOpen;
            private set { if (_isCommitOpen != value) { _isCommitOpen = value; OnPropertyChanged(); } }
        }

        public bool IsCommitEditorVisible
        {
            get => _isCommitEditorVisible;
            private set { if (_isCommitEditorVisible != value) { _isCommitEditorVisible = value; OnPropertyChanged(); } }
        }

        public bool IsCommitBusy
        {
            get => _isCommitBusy;
            private set { if (_isCommitBusy != value) { _isCommitBusy = value; OnPropertyChanged(); } }
        }

        public string CommitMessage
        {
            get => _commitMessage;
            set { if (_commitMessage != value) { _commitMessage = value; OnPropertyChanged(); } }
        }

        public string CommitStatusText
        {
            get => _commitStatusText;
            private set { if (_commitStatusText != value) { _commitStatusText = value; OnPropertyChanged(); } }
        }

        public bool HasCommitWarning
        {
            get => _hasCommitWarning;
            private set { if (_hasCommitWarning != value) { _hasCommitWarning = value; OnPropertyChanged(); } }
        }

        public string CommitWarningMessage
        {
            get => _commitWarningMessage;
            private set { if (_commitWarningMessage != value) { _commitWarningMessage = value; OnPropertyChanged(); } }
        }

        public bool HasCommitResult
        {
            get => _hasCommitResult;
            private set { if (_hasCommitResult != value) { _hasCommitResult = value; OnPropertyChanged(); } }
        }

        public string CommitResultMessage
        {
            get => _commitResultMessage;
            private set { if (_commitResultMessage != value) { _commitResultMessage = value; OnPropertyChanged(); } }
        }

        public NotificationKind CommitResultKind
        {
            get => _commitResultKind;
            private set { if (_commitResultKind != value) { _commitResultKind = value; OnPropertyChanged(); } }
        }

        public bool IsPermissionsOpen
        {
            get => _isPermissionsOpen;
            private set { if (_isPermissionsOpen != value) { _isPermissionsOpen = value; OnPropertyChanged(); } }
        }

        public bool IsDocsOpen
        {
            get => _isDocsOpen;
            private set { if (_isDocsOpen != value) { _isDocsOpen = value; OnPropertyChanged(); } }
        }

        public string DocsContent
        {
            get => _docsContent;
            private set { if (_docsContent != value) { _docsContent = value; OnPropertyChanged(); } }
        }

        /// <summary>Raised whenever a message is appended, so the view can auto-scroll.</summary>
        public event Action MessageAppended;

        /// <summary>Raised when the documentation content changes, so the view can reset the scroll.</summary>
        public event Action DocsNavigated;

        /// <summary>Raised when settings are saved and the provider/tools were rebuilt.</summary>
        public event Action SettingsSaved;

        /// <summary>
        /// Вызывается перед <see cref="ThemeChanged"/> с новым значением темы, пока контрол ещё не
        /// объявил смену. Хост (студия) использует хук, чтобы пересобрать палитру <c>Studio.*</c> до
        /// того, как представление перекроет свой словарь через <c>ChatStudioBridge</c>: иначе мост
        /// скопирует кисти предыдущей темы и чат отстанет на один переключатель.
        /// </summary>
        public Action<StudioTheme> ThemeApplying { get; set; }

        /// <summary>Raised when the theme (<see cref="StudioTheme"/>) changes, so the view can swap its palette.</summary>
        public event Action ThemeChanged;

        // ------------------------------------------------------------------
        // Commands
        // ------------------------------------------------------------------

        public AsyncRelayCommand SendCommand { get; }
        public RelayCommand StopCommand { get; }
        public RelayCommand NewChatCommand { get; }
        public RelayCommand ShowHistoryCommand { get; }
        public RelayCommand BackToChatCommand { get; }
        public RelayCommand<ChatConversation> OpenConversationCommand { get; }
        public RelayCommand<ChatConversation> DeleteConversationCommand { get; }
        public RelayCommand ClearHistoryCommand { get; }
        public RelayCommand AttachFilesCommand { get; }
        public RelayCommand<ChatAttachment> RemoveAttachmentCommand { get; }
        public RelayCommand<ChatMessage> CopyMessageCommand { get; }
        public RelayCommand<string> AnswerQuestionCommand { get; }
        public RelayCommand SkipQuestionCommand { get; }
        public AsyncRelayCommand ToggleRecordingCommand { get; }
        public AsyncRelayCommand CompactNowCommand { get; }

        public AsyncRelayCommand CommitCommand { get; }
        public AsyncRelayCommand CommitSendCommand { get; }
        public AsyncRelayCommand CommitAutoGenerateCommand { get; }
        public RelayCommand CommitCloseCommand { get; }
        public RelayCommand CommitResultCloseCommand { get; }
        public RelayCommand CopyConversationCommand { get; }
        public RelayCommand ClearInputCommand { get; }
        public AsyncRelayCommand DoItCommand { get; }
        public AsyncRelayCommand SendErrorsCommand { get; }
        public RelayCommand UpdateMemoryCommand { get; }
        public RelayCommand PermissionsCommand { get; }
        public RelayCommand PermissionsCloseCommand { get; }
        public RelayCommand DocsCommand { get; }
        public RelayCommand DocsHomeCommand { get; }
        public RelayCommand DocsCloseCommand { get; }
        public RelayCommand RefreshPromptsCommand { get; }

        // Semantic mode accents: Plan = blue, Act = green, Help = red.
        private const string PlanAccentHex = "#2196F3";
        private const string ActAccentHex = "#4CAF50";
        private const string HelpAccentHex = "#F44336";

        private static string AccentHex(AgentMode mode)
        {
            return mode == AgentMode.Act ? ActAccentHex
                : mode == AgentMode.Help ? HelpAccentHex
                : PlanAccentHex;
        }

        private static Brush ModeBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private string GetSolutionDirectory()
        {
            return ChatEnvironment.SolutionPathProvider?.Invoke() ?? string.Empty;
        }

        private void RecreateAiProvider()
        {
            _contextUsageBinder.Detach();
            UnhookAiNotifiers();
            _ai = RPA.Chat.Services.AiProviderFactory.Create(_settings);
            HookAiNotifiers();
            _contextUsageBinder.Attach(_ai as ITokenUsageProvider, BuildContextBudget());
        }

        /// <summary>Persists settings and rebuilds the provider, speech pipeline and agent tools.</summary>
        public void ApplySettings()
        {
            RPA.Chat.Services.SettingsStore.Save(_settings);
            RecreateAiProvider();
            _stt = SpeechToTextFactory.Create(_settings);
            RebuildAgentTools();
            SettingsSaved?.Invoke();
        }

        private void HookAiNotifiers()
        {
            if (_ai is IMessageNotifier m) m.MessageSent += OnAiMessageSent;
            if (_ai is IRetryNotifier r) r.RetryAttempt += OnRetryAttempt;
        }

        private void UnhookAiNotifiers()
        {
            if (_ai is IMessageNotifier m) m.MessageSent -= OnAiMessageSent;
            if (_ai is IRetryNotifier r) r.RetryAttempt -= OnRetryAttempt;
        }

        public async Task SendAsync()
        {
            var text = (_inputText ?? "").Trim();
            var attachments = _chat.Attachments.ToList();
            if (text.Length == 0 && attachments.Count == 0)
                return;

            InputText = "";
            _chat.Attachments.Clear();
            OnPropertyChanged(nameof(HasAttachments));
            await SendChatTextAsync(text, attachments);
        }

        private void Stop()
        {
            _chatCts?.Cancel();
        }

        private async Task SendChatTextAsync(string text, IReadOnlyList<ChatAttachment> attachments)
        {
            if (_chatCts != null || _agentRunner == null) return;

            var images = new List<ChatImagePart>();
            var notes = new List<string>();
            var fileBlocks = new System.Text.StringBuilder();

            if (attachments != null)
            {
                foreach (var a in attachments)
                {
                    if (a == null) continue;
                    if (a.IsImage)
                    {
                        images.Add(a.ToImagePart());
                        notes.Add("Image: " + a.FileName);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(a.TextContent))
                        {
                            fileBlocks.AppendLine().AppendLine("Attached file: " + a.FileName)
                                .AppendLine("```").AppendLine(a.TextContent).AppendLine("```");
                        }
                        notes.Add("File: " + a.FileName);
                    }
                }
            }

            var promptText = (text ?? "").Trim();
            if (fileBlocks.Length > 0)
                promptText = string.IsNullOrWhiteSpace(promptText)
                    ? fileBlocks.ToString().TrimEnd()
                    : promptText.TrimEnd() + Environment.NewLine + fileBlocks.ToString().TrimEnd();

            var uiText = (text ?? "").Trim();
            if (notes.Count > 0)
                uiText = string.IsNullOrWhiteSpace(uiText)
                    ? string.Join("  ", notes)
                    : uiText + Environment.NewLine + string.Join("  ", notes);

            if (string.IsNullOrWhiteSpace(promptText) && images.Count == 0)
                return;

            var modeName = _chat.Mode.ToString();
            var history = BuildTranscript();
            _chat.EnsureActiveConversation();
            AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.User, Text = uiText });

            var thinking = new ChatMessage { Kind = ChatMessageKind.AgentText, Mode = modeName, IsThinking = true, IsPartial = true, Text = "Thinking…" };
            AppendChatMessage(thinking);

            var cts = new CancellationTokenSource();
            _chatCts = cts;
            IsSending = true;
            StatusText = "Thinking…";
            try
            {
                var progress = new Progress<ChatStreamDelta>(delta =>
                {
                    if (!string.IsNullOrEmpty(delta.ReasoningDelta))
                        thinking.Reasoning += delta.ReasoningDelta;
                });
                var result = await _agentRunner.RunAsync(history, promptText, images, _chat.Mode, BuildContextBudget(), cts.Token, progress);

                thinking.IsThinking = false;
                thinking.IsPartial = false;
                if (string.IsNullOrWhiteSpace(thinking.Reasoning) && !string.IsNullOrWhiteSpace(result.Reasoning))
                    thinking.Reasoning = result.Reasoning;

                if (thinking.HasReasoning) thinking.Text = string.Empty;
                else _chat.Messages.Remove(thinking);

                if (!string.IsNullOrWhiteSpace(result.FinalText))
                {
                    AppendChatMessage(new ChatMessage
                    {
                        Kind = _chat.Mode == AgentMode.Act ? ChatMessageKind.ResultAnswerAct : ChatMessageKind.ResultAnswerPlan,
                        Mode = modeName,
                        Text = result.FinalText
                    });
                }
                StatusText = "";
            }
            catch (OperationCanceledException)
            {
                thinking.IsThinking = false;
                if (_chat.Messages.Contains(thinking)) AppendChat("Request cancelled.");
                StatusText = "";
            }
            catch (Exception ex)
            {
                thinking.IsThinking = false;
                AppendExtensionError(ex);
                StatusText = "";
                ChatLog.Error(ex, "Agent chat request failed.");
            }
            finally
            {
                _chatCts = null;
                cts.Dispose();
                IsSending = false;
                SaveHistoryNow();
                PersistUiState();
            }
        }
        private List<ChatMessageData> BuildTranscript()
        {
            var transcript = new List<ChatMessageData>();
            foreach (var message in _chat.Messages)
            {
                if (string.IsNullOrWhiteSpace(message.Text)) continue;
                switch (message.Kind)
                {
                    case ChatMessageKind.User:
                        transcript.Add(new ChatMessageData { Role = "user", Content = message.Text });
                        break;
                    case ChatMessageKind.AgentText:
                    case ChatMessageKind.ResultAnswerPlan:
                    case ChatMessageKind.ResultAnswerAct:
                        transcript.Add(new ChatMessageData { Role = "assistant", Content = message.Text });
                        break;
                }
            }
            return transcript;
        }

        private ContextBudget BuildContextBudget()
        {
            return new ContextBudget
            {
                ContextWindowSize = _settings.ContextWindowSize > 0 ? _settings.ContextWindowSize : 409600,
                CompactThresholdFraction = _settings.CompactThreshold,
                ReserveFraction = _settings.ReserveFraction,
                KeepRecentMessages = _settings.KeepRecentMessages,
                ToolResultMaxChars = _settings.ToolResultMaxChars,
                UseToolRole = _settings.UseToolRole,
                MaxIterations = _settings.MaxIterations,
                AutoCompact = _settings.AutoCompact
            };
        }

        private ContextBudget BuildSubAgentBudget()
        {
            var budget = BuildContextBudget();
            budget.MaxIterations = _settings.SubagentMaxIterations > 0 ? _settings.SubagentMaxIterations : 4;
            budget.AutoCompact = false;
            return budget;
        }

        // ------------------------------------------------------------------
        // Agent tools
        // ------------------------------------------------------------------

        private void RebuildAgentTools()
        {
            UnhookAgentRunner();
            _toolRegistry = BuildToolRegistry();
            var toolCallingMode = string.Equals(_settings.ToolCallingMode, "Native", StringComparison.OrdinalIgnoreCase)
                ? ToolCallingMode.Native
                : ToolCallingMode.JsonChat;
            var allowed = BuildAllowedPermissions();
            var allowedWays = BuildAllowedWays();

            _agentRunner = new AgentRunner(
                _ai,
                _toolRegistry,
                m => AgentPromptBuilder.Build(m, _toolRegistry, toolCallingMode, allowed, allowedWays, _settings.UserName, _settings.AddressByName, ParseMemoryBankMode()),
                BuildContextBudget(),
                toolCallingMode: toolCallingMode,
                allowedPermissions: allowed,
                allowedWays: allowedWays);
            _agentRunner.ToolExecuted += OnAgentToolExecuted;
            _agentRunner.ContextCompacted += OnAgentContextCompacted;
            _agentRunner.RetryOccurred += OnAgentRetryOccurred;
        }

        private void UnhookAgentRunner()
        {
            if (_agentRunner == null) return;
            _agentRunner.ToolExecuted -= OnAgentToolExecuted;
            _agentRunner.ContextCompacted -= OnAgentContextCompacted;
            _agentRunner.RetryOccurred -= OnAgentRetryOccurred;
            _agentRunner = null;
        }

        private AgentToolRegistry BuildToolRegistry()
        {
            var factory = new AgentToolFactory(
                _ai,
                GetSolutionDirectory,
                ChatEnvironment.DocumentService,
                _buildFeedback,
                AskQuestionAsync,
                RunSubAgentAsync,
                _settings.AllowSubagents,
                _settings.SubagentMaxDepth);
            return factory.CreateRegistry();
        }

        private AgentPermissions BuildAllowedPermissions()
        {
            return AgentPermissionsPolicy.Resolve(_settings);
        }

        private WorkflowWriteWay BuildAllowedWays()
        {
            return WorkflowWritePolicy.Resolve(_settings);
        }

        private MemoryBankMode ParseMemoryBankMode()
        {
            var value = _settings?.MemoryBankMode;
            if (string.Equals(value, "Tool", StringComparison.OrdinalIgnoreCase)) return MemoryBankMode.Tool;
            if (string.Equals(value, "Convention", StringComparison.OrdinalIgnoreCase)) return MemoryBankMode.Convention;
            return MemoryBankMode.Disabled;
        }

        private async Task<string> RunSubAgentAsync(string systemPrompt, string task, CancellationToken ct)
        {
            var registry = _toolRegistry;
            if (registry == null)
                return "spawn_agent unavailable: the tool registry is not initialized.";

            var toolCallingMode = string.Equals(_settings.ToolCallingMode, "Native", StringComparison.OrdinalIgnoreCase)
                ? ToolCallingMode.Native
                : ToolCallingMode.JsonChat;
            var allowed = BuildAllowedPermissions();
            var allowedWays = BuildAllowedWays();
            var budget = BuildSubAgentBudget();

            var item = new SubagentStatusItem { Prompt = task };
            OnUi(() => AppendChatMessage(new ChatMessage
            {
                Kind = ChatMessageKind.Subagent,
                Payload = new SubagentPayload { Item = item },
                Mode = _chat.Mode.ToString()
            }));
            OnUi(() => item.Status = "running");

            var runner = new AgentRunner(
                _ai,
                registry,
                m => AgentPromptBuilder.Build(m, registry, toolCallingMode, allowed, allowedWays),
                budget,
                toolCallingMode: toolCallingMode,
                allowedPermissions: allowed,
                allowedWays: allowedWays);

            runner.ToolExecuted += (s, e) =>
            {
                var latest = string.IsNullOrWhiteSpace(e.DisplayResult) ? (e.ToolName ?? "") : (e.ToolName ?? "") + ": " + e.DisplayResult;
                OnUi(() => { item.ToolCalls += 1; item.LatestToolCall = latest; });
            };

            try
            {
                var result = await runner.RunAsync(Array.Empty<ChatMessageData>(), task, _chat.Mode, budget, ct);
                var finalText = result?.FinalText ?? "";
                OnUi(() => { item.Status = "completed"; item.Result = finalText; });
                return finalText;
            }
            catch (OperationCanceledException)
            {
                OnUi(() => item.Status = "cancelled");
                return "Sub-agent cancelled.";
            }
            catch (Exception ex)
            {
                OnUi(() => { item.Status = "failed"; item.Error = ex.Message; });
                return "Sub-agent failed: " + ex.Message;
            }
        }

        private Task<string> AskQuestionAsync(string question, IReadOnlyList<string> options, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _questionTcs = tcs;

            var message = new ChatMessage
            {
                Kind = ChatMessageKind.Question,
                Payload = new QuestionPayload { Question = question, Options = options ?? new List<string>() },
                Mode = _chat.Mode.ToString()
            };
            _activeQuestion = message;
            OnUi(() => AppendChatMessage(message));

            ct.Register(() => { if (!tcs.Task.IsCompleted) tcs.TrySetResult(SkipAnswerText); });
            return tcs.Task;
        }

        private void AnswerQuestion(string option)
        {
            // The shared message control writes the selected option into the payload before
            // raising QuestionAnswered, so resolve the pending prompt with the recorded answer.
            if (_activeQuestion?.Payload is QuestionPayload q)
            {
                if (string.IsNullOrEmpty(q.Answer))
                    q.Answer = option;
                _questionTcs?.TrySetResult(q.Answer ?? option);
            }
        }

        private void SkipQuestion()
        {
            if (_activeQuestion?.Payload is QuestionPayload q)
            {
                q.IsSkipped = true;
                if (string.IsNullOrEmpty(q.Answer))
                    q.Answer = SkipAnswerText;
                _questionTcs?.TrySetResult(SkipAnswerText);
            }
        }

        private void OnAgentToolExecuted(object sender, AgentToolCall e)
        {
            OnUi(() =>
            {
                var detail = string.IsNullOrWhiteSpace(e.DisplayResult) ? e.Result : e.DisplayResult;
                ChatLog.Info("Agent tool executed: " + (e.ToolName ?? "") + (e.Blocked ? " (blocked)" : ""));
                if (string.Equals(e.ToolName, "ask_question", StringComparison.OrdinalIgnoreCase)) return;
                if (string.Equals(e.ToolName, "spawn_agent", StringComparison.OrdinalIgnoreCase)) return;
                AppendToolCall(e.ToolName, detail, e.Blocked);
            });
        }

        private void OnAgentContextCompacted(object sender, EventArgs e)
        {
            OnUi(() => AppendCompaction("Context compacted (summarized) to fit the context window."));
        }

        private void OnAgentRetryOccurred(object sender, AgentRetryArgs e)
        {
            ChatLog.Warn("Agent retry: " + (e.Kind ?? "") + " attempt " + e.Attempt + "/" + e.AttemptLimit + " - " + (e.Detail ?? ""));
        }

        // ------------------------------------------------------------------
        // Message appenders
        // ------------------------------------------------------------------

        private void AppendChatMessage(ChatMessage message)
        {
            _chat.AppendMessage(message);
            MessageAppended?.Invoke();
            ScheduleHistorySave();
        }

        private void AppendChat(string text) => AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.SystemNotice, Text = text });

        private void AppendExtensionError(string text) => AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.Error, Text = text });

        private void AppendExtensionError(Exception ex) => AppendExtensionError(ex?.Message ?? "Unknown error");

        private void AppendToolCall(string toolName, string result, bool blocked) =>
            AppendChatMessage(new ChatMessage
            {
                Kind = blocked ? ChatMessageKind.ToolBlocked : ChatMessageKind.ToolCall,
                Payload = new ToolCallPayload { ToolName = toolName ?? "", Result = result ?? "", Blocked = blocked }
            });

        private void AppendCompaction(string text) => AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.Compaction, Text = text });

        private void AppendModeSwitch(string text) => AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.ModeSwitch, Text = text });

        // ------------------------------------------------------------------
        // Token usage / provider notifications
        // ------------------------------------------------------------------

        private void OnMessagesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(PinnedMessageText));
            OnPropertyChanged(nameof(IsPinnedMessageVisible));
        }

        private void OnAiMessageSent(object sender, AiMessageEventArgs e)
        {
            ChatLog.Info("AI message sent (" + (e?.Role ?? "") + "): " + (e?.Content ?? "").Length + " chars.");
        }

        private void OnRetryAttempt(object sender, RetryAttemptEventArgs e)
        {
            ChatLog.Warn("Provider retry attempt " + (e?.Attempt ?? 0) + " (status " + (e?.StatusCode?.ToString() ?? "n/a") + ").");
            if (e != null && e.WillRetry)
                OnUi(() => ShowNotice("Provider retry attempt " + e.Attempt + " (HTTP " + (e.StatusCode?.ToString() ?? "n/a") + ").", NotificationKind.Warning));
        }

        private void ShowNotice(string message, NotificationKind kind = NotificationKind.Info)
        {
            NoticeKind = kind;
            NoticeMessage = message ?? "";
            HasNotice = true;
        }

        // ------------------------------------------------------------------
        // Mode / attachments / voice / compaction
        // ------------------------------------------------------------------

        private void ApplyModeName(string value)
        {
            var newMode = value == "Act" ? AgentMode.Act : (value == "Help" ? AgentMode.Help : AgentMode.Plan);
            if (newMode == _chat.Mode) return;
            if (_restoringUiState)
                _chat.RestoreMode(newMode);
            else
                _chat.SwitchMode(newMode);
            OnPropertyChanged(nameof(ModeName));
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(ModeAccentBrush));
            if (_restoringUiState) return;
            PersistUiState();
        }

        private void AttachFiles()
        {
            var dlg = new OpenFileDialog { Title = "Attach image or text file", Multiselect = true, CheckFileExists = true };
            if (dlg.ShowDialog() != true) return;
            foreach (var path in dlg.FileNames)
                AddAttachmentFile(path);
        }

        private void AddAttachmentFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".bmp" || ext == ".webp")
                {
                    var bytes = File.ReadAllBytes(path);
                    _chat.Attachments.Add(new ChatAttachment
                    {
                        FileName = Path.GetFileName(path),
                        IsImage = true,
                        MediaType = MediaTypeFor(ext),
                        DataBase64 = Convert.ToBase64String(bytes)
                    });
                }
                else
                {
                    // ТЗ-02: docx/pdf/xlsx распознаются общим reader'ом (одна логика с spec_import и read_files).
                    // Ошибка чтения — уведомление с причиной, а не zip-мусор в промпте и не молчание.
                    var document = DocumentTextReaders.Read(path, DocumentTextReadOptions.Default.WithMaxChars(20000));
                    if (!document.Ok && document.Status != DocumentTextStatus.Unsupported)
                    {
                        ShowNotice("Failed to attach " + Path.GetFileName(path) + ": " + document.Describe(),
                            NotificationKind.Error);
                        return;
                    }

                    var text = document.Status == DocumentTextStatus.Unsupported
                        ? File.ReadAllText(path)
                        : document.Text;
                    if (text.Length > 20000)
                        text = SafeText.Truncate(text, 20000) + "\n…(truncated)";
                    _chat.Attachments.Add(new ChatAttachment { FileName = Path.GetFileName(path), TextContent = text });
                }
                OnPropertyChanged(nameof(HasAttachments));
            }
            catch (Exception ex)
            {
                ShowNotice("Failed to attach " + Path.GetFileName(path) + ": " + ex.Message, NotificationKind.Error);
            }
        }

        private static string MediaTypeFor(string ext)
        {
            switch (ext)
            {
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".bmp": return "image/bmp";
                case ".webp": return "image/webp";
                default: return "image/png";
            }
        }

        private async Task ToggleRecordingAsync()
        {
            if (_isTranscribing) return;

            if (!_isRecording)
            {
                try { _mic.Start(); }
                catch (Exception ex)
                {
                    ShowNotice("Microphone unavailable: " + ex.Message, NotificationKind.Error);
                    return;
                }
                IsRecording = true;
                return;
            }

            byte[] audio;
            try { audio = _mic.Stop(); }
            catch (Exception ex)
            {
                IsRecording = false;
                ShowNotice("Failed to stop recording: " + ex.Message, NotificationKind.Error);
                return;
            }
            IsRecording = false;

            if (audio == null || audio.Length == 0)
            {
                AppendSystemMessage("No speech detected.");
                return;
            }

            IsTranscribing = true;
            try
            {
                var language = (_settings.SpeechToTextLanguage ?? "ru").Trim();
                var text = await _stt.TranscribeAsync(audio, language, CancellationToken.None);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (_settings.SpeechAutoSend && string.IsNullOrWhiteSpace(InputText))
                    {
                        InputText = text;
                        await SendAsync();
                    }
                    else
                    {
                        InputText = string.IsNullOrWhiteSpace(InputText)
                            ? text
                            : InputText.TrimEnd() + Environment.NewLine + text;
                    }
                }
                else
                {
                    AppendSystemMessage("No speech recognized.");
                }
            }
            catch (Exception ex)
            {
                ShowNotice("Speech recognition failed: " + ex.Message, NotificationKind.Error);
            }
            finally
            {
                IsTranscribing = false;
            }
        }

        private void OnMicLevelChanged(object sender, double level)
        {
            OnUi(() => MicLevel = _isRecording ? 0.6 + level * 0.4 : 1.0);
        }

        public double MicLevel { get; private set; } = 1.0;

        private async Task CompactNowAsync()
        {
            if (_agentRunner == null || _chatCts != null) return;
            var transcript = BuildTranscript();
            if (transcript.Count == 0)
            {
                AppendChat("Nothing to compact yet.");
                return;
            }

            try
            {
                var condensed = await _agentRunner.CompactAsync(transcript, BuildContextBudget(), CancellationToken.None);
                var compacted = condensed.Select(m => new ChatMessage
                {
                    Kind = m.Role == "assistant" ? ChatMessageKind.AgentText
                        : m.Role == "user" ? ChatMessageKind.User
                        : ChatMessageKind.SystemNotice,
                    Text = m.Content
                }).ToList();
                _chat.ApplyCompactedTranscript(compacted);
                AppendCompaction("Context compacted (summarized).");
                SaveHistoryNow();
            }
            catch (Exception ex)
            {
                AppendExtensionError("Compact failed: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // History
        // ------------------------------------------------------------------

        private void SaveHistoryNow()
        {
            _chat.SaveHistoryNow();
        }

        private void ScheduleHistorySave() => SaveHistoryNow();

        private void StartNewChat(bool markDirty = true)
        {
            _chatCts?.Cancel();
            _chat.StartNewChat(markDirty);
            ShowConversationView();
            PersistUiState();
        }

        private void OpenConversation(string id, bool markDirty = true)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            if (id == _chat.ActiveConversationId) return;

            _chatCts?.Cancel();
            if (!_chat.OpenConversation(id, markDirty))
                return;
            _activeQuestion = null;
            _questionTcs = null;
            ShowConversationView();
            PersistUiState();
        }

        private void DeleteConversation(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            _chat.DeleteConversation(id);
        }

        private void ClearHistory()
        {
            _chat.ClearHistory();
        }

        private void RefreshHistoryList()
        {
            _chat.RefreshHistory();
        }

        private void ShowConversationView()
        {
            _chat.ShowConversationView();
            OnPropertyChanged(nameof(IsConversationView));
            OnPropertyChanged(nameof(IsPinnedMessageVisible));
        }

        private void ShowHistoryView()
        {
            _chat.ShowHistoryView();
            OnPropertyChanged(nameof(IsConversationView));
            OnPropertyChanged(nameof(IsPinnedMessageVisible));
        }

        // ------------------------------------------------------------------
        // UI state / misc
        // ------------------------------------------------------------------

        private void RestoreUiState()
        {
            _restoringUiState = true;
            try
            {
                var state = _chat.LoadUiState();
                _chat.RestoreMode(state.AgentMode);
                OnPropertyChanged(nameof(ModeName));
                OnPropertyChanged(nameof(Mode));
                OnPropertyChanged(nameof(ModeAccentBrush));

                if (state.ChatViewMode == ChatViewMode.History)
                {
                    ShowHistoryView();
                }
                else
                {
                    var toOpen = _chat.ResolveConversationToOpen(state.LastConversationId);
                    if (toOpen != null) OpenConversation(toOpen.Id, markDirty: false);
                    else StartNewChat(markDirty: false);
                    ShowConversationView();
                }
            }
            finally
            {
                _restoringUiState = false;
            }
        }

        private void PersistUiState()
        {
            if (_restoringUiState) return;
            _chat.PersistUiState(MainTab.Chat);
        }

        private void CopyMessage(ChatMessage message)
        {
            if (message == null) return;
            try { System.Windows.Clipboard.SetText(message.DisplayText); }
            catch { /* clipboard busy — ignore */ }
        }

        private void RemoveAttachment(ChatAttachment attachment)
        {
            if (attachment != null && _chat.Attachments.Remove(attachment))
                OnPropertyChanged(nameof(HasAttachments));
        }

        public void AppendSystemMessage(string text) => OnUi(() => AppendChat(text));

        public void ClearNotice()
        {
            HasNotice = false;
            NoticeMessage = "";
        }

        private void OnUi(Action action)
        {
            var ctx = _uiContext;
            if (ctx == null || SynchronizationContext.Current == ctx)
                action();
            else
                ctx.Post(_ => action(), null);
        }

        // ------------------------------------------------------------------
        // Copy conversation / clear input / do-it / send errors
        // ------------------------------------------------------------------

        private void CopyConversation()
        {
            var sb = new StringBuilder();
            foreach (var m in _chat.Messages)
            {
                if (sb.Length > 0) sb.AppendLine().AppendLine();
                sb.Append(FormatMessageForCopy(m));
            }
            if (sb.Length == 0) return;
            try { Clipboard.SetText(sb.ToString()); }
            catch { /* clipboard busy — ignore */ }
        }

        private static string FormatMessageForCopy(ChatMessage message)
        {
            var sb = new StringBuilder();
            sb.Append(message.DisplayText);
            if (!string.IsNullOrWhiteSpace(message.Reasoning))
            {
                sb.AppendLine();
                sb.AppendLine("[Reasoning]");
                sb.Append(message.Reasoning);
            }
            return sb.ToString();
        }

        private void ClearInput()
        {
            InputText = "";
        }

        private async Task DoItAsync()
        {
            InputText = "Do it!!!";
            await SendAsync();
        }
        /// <summary>
        /// Отправляет готовый промпт в чат (используется студией для «отправить ошибку дизайнера»).
        /// Текст ставится в поле ввода и отправляется так же, как сообщение пользователя.
        /// </summary>
        public async Task SendPromptAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            InputText = text;
            await SendAsync();
        }



        private async Task SendErrorsAsync()
        {
            try
            {
                var errors = await _buildFeedback.GetCompilationErrorsAsync();
                var text = await BuildCompilationErrorsContextAsync(errors);
                if (string.IsNullOrWhiteSpace(text))
                {
                    AppendChat(await FormatNoCompilationErrorsAsync());
                    return;
                }

                await SendChatTextAsync(text, null);
            }
            catch (Exception ex)
            {
                AppendExtensionError(ex);
            }
        }

        private async Task<string> BuildCompilationErrorsContextAsync(IReadOnlyList<BuildError> errors)
        {
            return await CompilationErrorContextBuilder.BuildAsync(
                errors,
                (file, line, ctx) => Task.FromResult(ReadDocumentWindow(file, line, ctx)),
                MaxErrorsWithContext,
                ErrorContextLines);
        }

        private static string ReadDocumentWindow(string file, int line, int contextLines)
        {
            try
            {
                var window = ChatEnvironment.DocumentService?.ReadOpenDocumentWindow(file, line, contextLines);
                if (!string.IsNullOrWhiteSpace(window))
                    return window;

                if (!File.Exists(file))
                    return null;

                var lines = File.ReadAllLines(file);
                if (line < 1 || line > lines.Length)
                    return null;

                var from = Math.Max(1, line - contextLines);
                var to = Math.Min(lines.Length, line + contextLines);
                var sb = new StringBuilder();
                for (var i = from; i <= to; i++)
                    sb.AppendLine(lines[i - 1]);
                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }

        private async Task<string> FormatNoCompilationErrorsAsync()
        {
            var sb = new StringBuilder();
            sb.AppendLine("The build Error List is empty.");

            string output;
            try
            {
                output = await _buildFeedback.GetOutputWindowTextAsync();
            }
            catch (Exception ex)
            {
                output = "Build output unavailable: " + ex.Message;
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                sb.AppendLine();
                sb.AppendLine("Build output:");
                sb.AppendLine(output);
            }

            return sb.ToString().TrimEnd();
        }

        // ------------------------------------------------------------------
        // Commit workflow
        // ------------------------------------------------------------------

        private async Task CommitAsync()
        {
            try
            {
                CommitStatusText = "";
                HideCommitResult();
                var solutionDir = GetSolutionDirectory();
                if (string.IsNullOrWhiteSpace(solutionDir))
                {
                    ShowCommitWarning("No open solution. Open a solution and retry.");
                    return;
                }

                var repoRoot = await _git.GetRepositoryRootAsync(solutionDir);
                var branch = await _git.GetCurrentBranchAsync(repoRoot);
                if (!string.Equals(branch, CommitBranchName, StringComparison.Ordinal))
                {
                    ShowCommitWarning("Commit is only available on the '" + CommitBranchName + "' branch. Current branch: " + (string.IsNullOrWhiteSpace(branch) ? "(unknown)" : branch));
                    return;
                }

                ShowCommitEditor();
            }
            catch (Exception ex)
            {
                ShowCommitWarning("Could not open the commit control: " + ex.Message);
            }
        }

        private async Task CommitSendAsync()
        {
            var message = CommitMessage.Trim();
            if (message.Length == 0)
            {
                CommitStatusText = "Commit message is required.";
                return;
            }

            SetCommitBusy(true);
            CommitStatusText = "Committing…";
            try
            {
                var solutionDir = GetSolutionDirectory();
                if (string.IsNullOrWhiteSpace(solutionDir))
                {
                    ShowCommitWarning("No open solution. Open a solution and retry.");
                    return;
                }

                var repoRoot = await _git.GetRepositoryRootAsync(solutionDir);
                var branch = await _git.GetCurrentBranchAsync(repoRoot);
                if (!string.Equals(branch, CommitBranchName, StringComparison.Ordinal))
                {
                    ShowCommitWarning("Commit is only available on the '" + CommitBranchName + "' branch. Current branch: " + (string.IsNullOrWhiteSpace(branch) ? "(unknown)" : branch));
                    return;
                }

                await _git.CommitAllAsync(repoRoot, message);

                CommitMessage = "";
                CloseCommitControl();
                ShowCommitResult("Committed to " + CommitBranchName + ": " + message, true);
            }
            catch (Exception ex)
            {
                ShowCommitResult(ex.Message, false);
            }
            finally
            {
                SetCommitBusy(false);
            }
        }

        private async Task CommitAutoGenerateAsync()
        {
            if (_ai == null)
            {
                CommitStatusText = "AI provider is not available.";
                return;
            }

            SetCommitBusy(true);
            CommitStatusText = "Generating commit message…";
            try
            {
                var solutionDir = GetSolutionDirectory();
                if (string.IsNullOrWhiteSpace(solutionDir))
                {
                    ShowCommitWarning("No open solution. Open a solution and retry.");
                    return;
                }

                var repoRoot = await _git.GetRepositoryRootAsync(solutionDir);
                var message = await GenerateCommitMessageAsync(repoRoot);
                if (string.IsNullOrWhiteSpace(message))
                {
                    CommitStatusText = "No changes detected.";
                    return;
                }

                CommitMessage = message;
                CommitStatusText = "";
            }
            catch (Exception ex)
            {
                CommitStatusText = ex.Message;
            }
            finally
            {
                SetCommitBusy(false);
            }
        }

        private async Task<string> GenerateCommitMessageAsync(string repoRoot)
        {
            var changedFiles = await _git.GetChangedFilesAsync(repoRoot);
            if (changedFiles.Count == 0)
                return null;

            var plan = CommitMessageBuilder.GroupChangedFiles(repoRoot, changedFiles);
            if (plan.Projects.Count == 0 && plan.OtherFiles.Count == 0)
                return null;

            if (plan.Projects.Count == 0)
            {
                var diff = await _git.GetDiffForMessageAsync(repoRoot);
                if (string.IsNullOrWhiteSpace(diff))
                    return null;
                return await SummarizeChangesAsync(diff, CommitMessageSystemPrompt);
            }

            var parts = new List<string>();
            foreach (var project in plan.Projects)
            {
                var diff = await _git.GetDiffForPathAsync(repoRoot, project.RelativePath);
                var material = BuildProjectChangeMaterial(project.Name, diff, project.Files);
                var summary = await SummarizeChangesAsync(material, CommitOtherSummarySystemPrompt);
                parts.Add(CommitMessageBuilder.FormatProjectLine(project.Name, summary));
            }
            if (plan.OtherFiles.Count > 0)
            {
                var summary = await SummarizeChangesAsync(BuildOtherChangeMaterial(plan.OtherFiles), CommitOtherSummarySystemPrompt);
                parts.Add(CommitMessageBuilder.FormatChoreLine(summary));
            }
            return CommitMessageBuilder.JoinLines(parts);
        }

        private static string BuildProjectChangeMaterial(string projectName, string diff, IReadOnlyList<string> files)
        {
            var sb = new StringBuilder();
            sb.Append("Project: ").AppendLine(projectName);
            if (!string.IsNullOrWhiteSpace(diff))
            {
                sb.AppendLine("Diff:");
                sb.AppendLine(diff.TrimEnd());
            }
            sb.AppendLine("Changed files:");
            foreach (var f in files)
                sb.AppendLine(f);
            return sb.ToString();
        }

        private static string BuildOtherChangeMaterial(IReadOnlyList<string> files)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Changed files (outside projects):");
            foreach (var f in files)
                sb.AppendLine(f);
            return sb.ToString();
        }

        private async Task<string> SummarizeChangesAsync(string content, string systemPrompt)
        {
            if (content.Length > CommitDiffMaxChars)
                content = SafeText.Truncate(content, CommitDiffMaxChars) + "\n… (diff truncated)";
            var messages = new List<ChatMessageData>
            {
                new ChatMessageData { Role = "user", Content = content }
            };
            var response = await _ai.ChatAsync(messages, systemPrompt);
            var text = response?.Answer?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("The AI provider returned an empty message.");
            return text;
        }

        private void ShowCommitEditor()
        {
            OnUi(() =>
            {
                IsCommitOpen = true;
                IsCommitEditorVisible = true;
                HasCommitWarning = false;
                CommitMessage = "";
                CommitStatusText = "";
            });
        }

        private void CloseCommitControl()
        {
            OnUi(() =>
            {
                IsCommitOpen = false;
                IsCommitEditorVisible = false;
                HasCommitWarning = false;
            });
        }

        private void SetCommitBusy(bool busy)
        {
            OnUi(() => IsCommitBusy = busy);
        }

        private void ShowCommitResult(string message, bool success)
        {
            OnUi(() =>
            {
                CommitResultKind = success ? NotificationKind.Success : NotificationKind.Error;
                CommitResultMessage = message ?? "";
                HasCommitResult = true;
            });
        }

        private void HideCommitResult()
        {
            OnUi(() =>
            {
                HasCommitResult = false;
                CommitResultMessage = "";
            });
        }

        private void ShowCommitWarning(string message)
        {
            OnUi(() =>
            {
                IsCommitOpen = true;
                IsCommitEditorVisible = false;
                HasCommitWarning = true;
                CommitWarningMessage = message ?? "";
            });
        }

        // ------------------------------------------------------------------
        // Memory bank / permissions / docs
        // ------------------------------------------------------------------

        private void UpdateMemory()
        {
            InputText = ParseMemoryBankMode() == MemoryBankMode.Convention
                ? "update memory bank: read memory-bank/*.md files with read_files, then update the relevant files with editor."
                : "update memory bank: read the current memory bank with read_memory, then update the relevant files with update_memory.";
        }

        private void OpenPermissions()
        {
            OnUi(() => IsPermissionsOpen = true);
        }

        private void ClosePermissions()
        {
            OnUi(() => IsPermissionsOpen = false);
            RPA.Chat.Services.SettingsStore.Save(_settings);
            RebuildAgentTools();
        }

        private void OpenDocs()
        {
            ShowDocsIndex();
            OnUi(() => IsDocsOpen = true);
        }

        private void ShowDocsIndex()
        {
            OnUi(() => SetDocsContent(DocumentationViewer.IndexFileName, _docsViewer.ReadIndex()));
        }

        private void CloseDocs()
        {
            MarkdownRenderer.RelativeLinkNavigator = null;
            OnUi(() => IsDocsOpen = false);
        }

        private bool NavigateDocs(string target)
        {
            var path = _docsViewer.ResolveLink(_docsCurrentPath, target);
            if (string.IsNullOrEmpty(path))
                return false;

            OnUi(() => SetDocsContent(path, _docsViewer.ReadDocument(path)));
            return true;
        }

        private void SetDocsContent(string path, string content)
        {
            _docsCurrentPath = path;
            MarkdownRenderer.RelativeLinkNavigator = NavigateDocs;
            DocsContent = content;
            DocsNavigated?.Invoke();
        }

        private void RefreshPrompts()
        {
            if (_ai is IPromptReloadable reloadable)
            {
                reloadable.ReloadPromptsFromDisk();
                AppendChat("Prompts reloaded from disk: " + RPA.Shared.Services.PromptPaths.ResolvePromptsDirectory());
            }
            else
            {
                AppendChat("Prompts reload is unavailable for the current provider (stub/offline).");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

