using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Newtonsoft.Json;
using RPA.AiAgent.Models;
using RPA.AiAgent.Services;

namespace RPA.AiAgent.ViewModels
{
    /// <summary>
    /// ViewModel для контрола чата с AI
    /// </summary>
    public class AiAgentChatViewModel : INotifyPropertyChanged
    {
        private readonly AiProviderFactory _providerFactory;
        private readonly IConversationStorage _conversationStorage;
        private readonly IRpaAiIntegrationService _rpaIntegration;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private IAiProvider _currentProvider;
        private AllProviderSettings _allProviderSettings;

        private string _userInput;
        private bool _isSending;
        private AiModel _selectedModel;
        private string _statusMessage;
        private ProviderType _selectedProvider;
        private bool _isSettingsVisible;
        private bool _isHistoryVisible;
        private string _ollamaUrl = "http://localhost:11434";
        private double _temperature = 0.7;
        private int? _maxTokens;
        private bool _autoSaveConversations = true;
        private bool _showTokenStats = true;
        private bool _enableSoundNotifications = false;
        private bool _isMcpAutoApproveVisible;
        private bool _isGlobalAutoApproveEnabled;
        private bool _mcpToolPermissionsLoaded;
        private bool _isLoadingMcpToolPermissions;

        public AiAgentChatViewModel(
            AiProviderFactory providerFactory,
            IConversationStorage conversationStorage,
            IRpaAiIntegrationService rpaIntegration)
        {
            _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
            _conversationStorage = conversationStorage ?? throw new ArgumentNullException(nameof(conversationStorage));
            _rpaIntegration = rpaIntegration ?? throw new ArgumentNullException(nameof(rpaIntegration));

            Messages = new ObservableCollection<ChatMessage>();
            AvailableModels = new ObservableCollection<AiModel>();
            ModelDisplayNames = new ObservableCollection<string>();
            AvailableProviders = new ObservableCollection<ProviderType>(_providerFactory.GetAvailableProviders());
            CurrentConversation = new Conversation();
            ConversationHistory = new ObservableCollection<Conversation>();
            McpToolPermissions = new ObservableCollection<McpToolPermission>();

            // Инициализация команд
            SendMessageCommand = new RelayCommand(async _ => await SendMessageAsync(), _ => CanSendMessage());
            ClearChatCommand = new RelayCommand(_ => ClearChat(), _ => Messages.Count > 0);
            LoadModelsCommand = new RelayCommand(async _ => await LoadModelsAsync());
            SaveConversationCommand = new RelayCommand(_ => CopyConversationToClipboard(), _ => Messages.Count > 0);
            CancelGenerationCommand = new RelayCommand(_ => CancelGeneration(), _ => IsSending);
            ShowAvailableActivitiesCommand = new RelayCommand(async _ => await ShowAvailableActivitiesAsync());
            SettingsCommand = new RelayCommand(_ => ToggleSettings());
            CloseSettingsCommand = new RelayCommand(_ => CloseSettings());
            SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
            ResetSettingsCommand = new RelayCommand(_ => ResetSettings());
            TestOllamaConnectionCommand = new RelayCommand(async _ => await TestOllamaConnectionAsync());
            HistoryCommand = new RelayCommand(_ => ToggleHistory());
            CloseHistoryCommand = new RelayCommand(_ => CloseHistory());
            LoadConversationCommand = new RelayCommand(async param => await LoadConversationAsync(param as Conversation));
            DeleteConversationCommand = new RelayCommand(async param => await DeleteConversationAsync(param as Conversation));
            RefreshHistoryCommand = new RelayCommand(async _ => await RefreshHistoryAsync());
            CopyMessageCommand = new RelayCommand(param => CopyMessageToClipboard(param as ChatMessage));
            ToggleMcpAutoApproveCommand = new RelayCommand(_ => ToggleMcpAutoApprove());
            CloseMcpAutoApproveCommand = new RelayCommand(_ => CloseMcpAutoApprove());
            SelectAllToolsCommand = new RelayCommand(_ => SelectAllTools());
            SelectNoneToolsCommand = new RelayCommand(_ => SelectNoneTools());

            // Загружаем сохраненные настройки
            LoadSettings();

            // Загружаем MCP разрешения сразу при инициализации (чтобы ApprovedToolsCount и IsToolApproved работали корректно)
            _ = Task.Run(async () => await LoadMcpToolPermissionsAsync());

            // Инициализация провайдера по умолчанию
            _ = Task.Run(async () => await InitializeDefaultProviderAsync());

            // Автоматически загружаем список MCP инструментов
            _ = Task.Run(async () => await LoadMcpToolsAsync());
        }

        #region Properties

        /// <summary>
        /// Коллекция сообщений в чате
        /// </summary>
        public ObservableCollection<ChatMessage> Messages { get; }

        /// <summary>
        /// Доступные модели от текущего провайдера
        /// </summary>
        public ObservableCollection<AiModel> AvailableModels { get; }

        /// <summary>
        /// Список имен моделей для SearchableComboBox
        /// </summary>
        public ObservableCollection<string> ModelDisplayNames { get; }

        /// <summary>
        /// Доступные провайдеры
        /// </summary>
        public ObservableCollection<ProviderType> AvailableProviders { get; }

        /// <summary>
        /// Текущий диалог
        /// </summary>
        public Conversation CurrentConversation { get; set; }

        /// <summary>
        /// История диалогов
        /// </summary>
        public ObservableCollection<Conversation> ConversationHistory { get; }

        /// <summary>
        /// Текст ввода пользователя
        /// </summary>
        public string UserInput
        {
            get => _userInput;
            set
            {
                if (_userInput != value)
                {
                    _userInput = value;
                    OnPropertyChanged(nameof(UserInput));
                }
            }
        }

        /// <summary>
        /// Флаг отправки сообщения
        /// </summary>
        public bool IsSending
        {
            get => _isSending;
            set
            {
                if (_isSending != value)
                {
                    _isSending = value;
                    OnPropertyChanged(nameof(IsSending));
                }
            }
        }

        /// <summary>
        /// Выбранная модель
        /// </summary>
        public AiModel SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (_selectedModel != value)
                {
                    _selectedModel = value;
                    OnPropertyChanged(nameof(SelectedModel));
                    OnPropertyChanged(nameof(SelectedModelDisplayName));
                    if (value != null)
                    {
                        CurrentConversation.ModelName = value.Name;
                        // Сохраняем выбранную модель для текущего провайдера
                        SaveSelectedModelForCurrentProvider();
                    }
                }
            }
        }

        /// <summary>
        /// Выбранное имя модели для SearchableComboBox
        /// </summary>
        public string SelectedModelDisplayName
        {
            get => SelectedModel?.DisplayName;
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    AiModel model = AvailableModels.FirstOrDefault(m => m.DisplayName == value);
                    if (model != null && model != SelectedModel)
                    {
                        SelectedModel = model;
                    }
                }
            }
        }

        /// <summary>
        /// Выбранный провайдер
        /// </summary>
        public ProviderType SelectedProvider
        {
            get => _selectedProvider;
            set
            {
                if (_selectedProvider != value)
                {
                    // Сохраняем текущую модель перед переключением провайдера
                    SaveSelectedModelForCurrentProvider();

                    _selectedProvider = value;
                    _currentProvider = _providerFactory.GetProvider(value);
                    _providerFactory.SaveSelectedProvider(value);

                    // Мгновенно очищаем список моделей и выбранную модель
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        AvailableModels.Clear();
                        ModelDisplayNames.Clear();
                        SelectedModel = null;
                    });

                    OnPropertyChanged(nameof(SelectedProvider));
                    _ = Task.Run(async () => await LoadModelsAsync());
                }
            }
        }

        /// <summary>
        /// Сообщение статуса
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged(nameof(StatusMessage));
                }
            }
        }

        private string _systemPrompt;

        /// <summary>
        /// Системный промпт для AI
        /// </summary>
        public string SystemPrompt
        {
            get => _systemPrompt;
            set
            {
                if (_systemPrompt != value)
                {
                    _systemPrompt = value;
                    OnPropertyChanged(nameof(SystemPrompt));
                }
            }
        }

        private int _sessionPromptTokens;
        private int _sessionCompletionTokens;
        private decimal _sessionCost;

        /// <summary>
        /// Количество prompt токенов за сеанс
        /// </summary>
        public int SessionPromptTokens
        {
            get => _sessionPromptTokens;
            set
            {
                if (_sessionPromptTokens != value)
                {
                    _sessionPromptTokens = value;
                    OnPropertyChanged(nameof(SessionPromptTokens));
                    OnPropertyChanged(nameof(SessionTotalTokens));
                    OnPropertyChanged(nameof(SessionContextInfo));
                }
            }
        }

        /// <summary>
        /// Количество completion токенов за сеанс
        /// </summary>
        public int SessionCompletionTokens
        {
            get => _sessionCompletionTokens;
            set
            {
                if (_sessionCompletionTokens != value)
                {
                    _sessionCompletionTokens = value;
                    OnPropertyChanged(nameof(SessionCompletionTokens));
                    OnPropertyChanged(nameof(SessionTotalTokens));
                    OnPropertyChanged(nameof(SessionContextInfo));
                }
            }
        }

        /// <summary>
        /// Общее количество токенов за сеанс
        /// </summary>
        public int SessionTotalTokens => SessionPromptTokens + SessionCompletionTokens;

        /// <summary>
        /// Стоимость за сеанс
        /// </summary>
        public decimal SessionCost
        {
            get => _sessionCost;
            set
            {
                if (_sessionCost != value)
                {
                    _sessionCost = value;
                    OnPropertyChanged(nameof(SessionCost));
                    OnPropertyChanged(nameof(SessionCostFormatted));
                }
            }
        }

        /// <summary>
        /// Форматированная стоимость за сеанс
        /// </summary>
        public string SessionCostFormatted => $"${SessionCost:F4}";

        /// <summary>
        /// Информация о контексте: использовано/максимум
        /// </summary>
        public string SessionContextInfo
        {
            get
            {
                if (SelectedModel?.ContextLength.HasValue == true)
                {
                    double contextInK = SelectedModel.ContextLength.Value / 1000.0;
                    double usedInK = SessionTotalTokens / 1000.0;
                    return $"{usedInK:F1}k / {contextInK:F1}k";
                }
                return $"{SessionTotalTokens / 1000.0:F1}k";
            }
        }

        /// <summary>
        /// Флаг видимости панели настроек
        /// </summary>
        public bool IsSettingsVisible
        {
            get => _isSettingsVisible;
            set
            {
                if (_isSettingsVisible != value)
                {
                    _isSettingsVisible = value;
                    OnPropertyChanged(nameof(IsSettingsVisible));
                }
            }
        }

        /// <summary>
        /// Флаг видимости панели истории
        /// </summary>
        public bool IsHistoryVisible
        {
            get => _isHistoryVisible;
            set
            {
                if (_isHistoryVisible != value)
                {
                    _isHistoryVisible = value;
                    OnPropertyChanged(nameof(IsHistoryVisible));
                }
            }
        }

        /// <summary>
        /// Наличие истории диалогов
        /// </summary>
        public bool HasConversationHistory => ConversationHistory?.Count > 0;

        /// <summary>
        /// URL сервера Ollama
        /// </summary>
        public string OllamaUrl
        {
            get => _ollamaUrl;
            set
            {
                if (_ollamaUrl != value)
                {
                    _ollamaUrl = value;
                    OnPropertyChanged(nameof(OllamaUrl));
                }
            }
        }

        /// <summary>
        /// Температура генерации (0-2)
        /// </summary>
        public double Temperature
        {
            get => _temperature;
            set
            {
                if (_temperature != value)
                {
                    _temperature = value;
                    OnPropertyChanged(nameof(Temperature));
                }
            }
        }

        /// <summary>
        /// Максимальное количество токенов
        /// </summary>
        public int? MaxTokens
        {
            get => _maxTokens;
            set
            {
                if (_maxTokens != value)
                {
                    _maxTokens = value;
                    OnPropertyChanged(nameof(MaxTokens));
                }
            }
        }

        /// <summary>
        /// Автосохранение диалогов
        /// </summary>
        public bool AutoSaveConversations
        {
            get => _autoSaveConversations;
            set
            {
                if (_autoSaveConversations != value)
                {
                    _autoSaveConversations = value;
                    OnPropertyChanged(nameof(AutoSaveConversations));
                }
            }
        }

        /// <summary>
        /// Показывать статистику токенов
        /// </summary>
        public bool ShowTokenStats
        {
            get => _showTokenStats;
            set
            {
                if (_showTokenStats != value)
                {
                    _showTokenStats = value;
                    OnPropertyChanged(nameof(ShowTokenStats));
                }
            }
        }

        /// <summary>
        /// Включить звуковые уведомления
        /// </summary>
        public bool EnableSoundNotifications
        {
            get => _enableSoundNotifications;
            set
            {
                if (_enableSoundNotifications != value)
                {
                    _enableSoundNotifications = value;
                    OnPropertyChanged(nameof(EnableSoundNotifications));
                }
            }
        }

        /// <summary>
        /// Флаг видимости панели MCP Auto-Approve
        /// </summary>
        public bool IsMcpAutoApproveVisible
        {
            get => _isMcpAutoApproveVisible;
            set
            {
                if (_isMcpAutoApproveVisible != value)
                {
                    _isMcpAutoApproveVisible = value;
                    OnPropertyChanged(nameof(IsMcpAutoApproveVisible));
                }
            }
        }

        /// <summary>
        /// Глобальный переключатель автоодобрения
        /// </summary>
        public bool IsGlobalAutoApproveEnabled
        {
            get => _isGlobalAutoApproveEnabled;
            set
            {
                if (_isGlobalAutoApproveEnabled != value)
                {
                    _isGlobalAutoApproveEnabled = value;
                    OnPropertyChanged(nameof(IsGlobalAutoApproveEnabled));
                    SaveMcpToolPermissions();
                }
            }
        }

        /// <summary>
        /// Коллекция разрешений MCP инструментов
        /// </summary>
        public ObservableCollection<McpToolPermission> McpToolPermissions { get; }

        /// <summary>
        /// Количество одобренных инструментов
        /// </summary>
        public int ApprovedToolsCount => McpToolPermissions?.Count(p => p.IsApproved) ?? 0;

        #endregion

        #region Commands

        public ICommand SendMessageCommand { get; }
        public ICommand ClearChatCommand { get; }
        public ICommand LoadModelsCommand { get; }
        public ICommand SaveConversationCommand { get; }
        public ICommand CancelGenerationCommand { get; }
        public ICommand ShowAvailableActivitiesCommand { get; }
        public ICommand SettingsCommand { get; }
        public ICommand CloseSettingsCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand ResetSettingsCommand { get; }
        public ICommand TestOllamaConnectionCommand { get; }
        public ICommand HistoryCommand { get; }
        public ICommand CloseHistoryCommand { get; }
        public ICommand LoadConversationCommand { get; }
        public ICommand DeleteConversationCommand { get; }
        public ICommand RefreshHistoryCommand { get; }
        public ICommand CopyMessageCommand { get; }
        public ICommand ToggleMcpAutoApproveCommand { get; }
        public ICommand CloseMcpAutoApproveCommand { get; }
        public ICommand SelectAllToolsCommand { get; }
        public ICommand SelectNoneToolsCommand { get; }

        #endregion

        #region Nested Classes

        /// <summary>
        /// Контекст для отслеживания итераций AI-взаимодействия
        /// </summary>
        private class AiInteractionContext
        {
            public int IterationCount { get; set; }
            public const int MaxIterations = 10; // Защита от бесконечных циклов
            public bool ShouldContinue => IterationCount < MaxIterations;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Основной метод отправки сообщения с поддержкой итеративной обработки MCP команд
        /// </summary>
        private async Task SendMessageAsync()
        {
            // === ЭТАП 1: Валидация и подготовка ===
            if (string.IsNullOrWhiteSpace(UserInput) || SelectedModel == null)
            {
                return;
            }

            string userMessage = UserInput;
            UserInput = string.Empty; // Очищаем поле ввода сразу

            // Добавляем сообщение пользователя
            ChatMessage userChatMessage = new ChatMessage
            {
                Role = "user",
                Content = userMessage,
                Timestamp = DateTime.Now
            };
            Messages.Add(userChatMessage);
            CurrentConversation.Messages.Add(userChatMessage);

            IsSending = true;
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            try
            {
                TimeSpan updateInterval = TimeSpan.FromMilliseconds(50);

                // === ЭТАП 2: Первый ответ от AI ===
                Debug.WriteLine("[SendMessage] Getting first AI response...");
                ChatMessage firstResponse = await SendToAiAndStreamResponseAsync(updateInterval, cancellationTokenSource);
                CurrentConversation.Messages.Add(firstResponse);

                // === ЭТАП 3: Цикл обработки MCP команд ===
                AiInteractionContext context = new AiInteractionContext();
                ChatMessage currentAiResponse = firstResponse;

                while (context.ShouldContinue)
                {
                    context.IterationCount++;
                    Debug.WriteLine($"[MCP-ITERATION-{context.IterationCount}] Processing AI response...");

                    // 3.1 Проверяем и выполняем MCP команды
                    RpaOperationResult mcpResult = await _rpaIntegration.ProcessAiResponseAsync(
                        currentAiResponse.Content,
                        IsToolApproved
                    );

                    // 3.2 Если нет команд или выполнение не требует продолжения - выходим
                    if (!ProcessMcpCommandResult(mcpResult))
                    {
                        Debug.WriteLine($"[MCP-ITERATION-{context.IterationCount}] No more commands, exiting loop");
                        break;
                    }

                    // 3.3 Отправляем результат обратно AI и получаем новый ответ
                    Debug.WriteLine($"[MCP-ITERATION-{context.IterationCount}] Getting next AI response...");
                    currentAiResponse = await SendToAiAndStreamResponseAsync(updateInterval, cancellationTokenSource);
                    CurrentConversation.Messages.Add(currentAiResponse);
                }

                if (context.IterationCount >= AiInteractionContext.MaxIterations)
                {
                    Debug.WriteLine($"[MCP-WARNING] Reached max iterations limit ({AiInteractionContext.MaxIterations})");
                    StatusMessage = $"⚠️ Достигнут лимит итераций ({AiInteractionContext.MaxIterations})";
                }
                else
                {
                    StatusMessage = "Ответ получен";
                }

                // === ЭТАП 4: Автосохранение ===
                if (AutoSaveConversations)
                {
                    Debug.WriteLine("[SendMessage] Auto-saving conversation...");
                    await SaveConversationAsync();
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Генерация отменена";
                Debug.WriteLine("[SendMessage] Operation cancelled by user");

                // Добавляем системное сообщение об отмене
                ChatMessage cancelMessage = new ChatMessage
                {
                    Role = "system",
                    Content = "⚠️ Генерация ответа была отменена пользователем",
                    Timestamp = DateTime.Now,
                    Type = MessageType.System
                };
                Messages.Add(cancelMessage);
                CurrentConversation.Messages.Add(cancelMessage);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка: {ex.Message}";
                Debug.WriteLine($"[SendMessage-ERROR] Exception: {ex.Message}");
                Debug.WriteLine($"[SendMessage-ERROR] Stack trace: {ex.StackTrace}");

                // Добавляем сообщение об ошибке
                ChatMessage errorMessage = new ChatMessage
                {
                    Role = "system",
                    Content = $"❌ Произошла ошибка: {ex.Message}",
                    Timestamp = DateTime.Now,
                    Type = MessageType.System
                };
                Messages.Add(errorMessage);
                CurrentConversation.Messages.Add(errorMessage);
            }
            finally
            {
                IsSending = false;
                cancellationTokenSource?.Dispose();
            }
        }

        /// <summary>
        /// Отправляет текущую историю сообщений AI и получает ответ с streaming
        /// </summary>
        private async Task<ChatMessage> SendToAiAndStreamResponseAsync(TimeSpan updateInterval, CancellationTokenSource cancellationTokenSource)
        {
            // Создаем сообщение для ответа AI
            ChatMessage assistantMessage = new ChatMessage
            {
                Role = "assistant",
                Content = string.Empty,
                Timestamp = DateTime.Now,
                IsStreaming = true
            };
            Messages.Add(assistantMessage);

            // Буфер для накопления чанков
            StringBuilder buffer = new StringBuilder();
            DateTime lastUpdateTime = DateTime.Now;

            // Подготавливаем сообщения для отправки
            System.Collections.Generic.List<ChatMessage> messagesToSend = Messages.Where(m => !m.IsStreaming).ToList();
            if (!string.IsNullOrEmpty(SystemPrompt))
            {
                messagesToSend.Insert(0, new ChatMessage
                {
                    Role = "system",
                    Content = SystemPrompt
                });
            }

            // Получаем ответ с потоковой передачей
            await _currentProvider.ChatStreamAsync(
                SelectedModel.Id,
                messagesToSend,
                chunk =>
                {
                    lock (buffer)
                    {
                        _ = buffer.Append(chunk);
                    }

                    // Обновляем UI с интервалом
                    TimeSpan timeSinceLastUpdate = DateTime.Now - lastUpdateTime;
                    if (timeSinceLastUpdate >= updateInterval)
                    {
                        lastUpdateTime = DateTime.Now;
                        string contentToAdd;
                        lock (buffer)
                        {
                            contentToAdd = buffer.ToString();
                            _ = buffer.Clear();
                        }

                        _ = Application.Current.Dispatcher.BeginInvoke(
                            DispatcherPriority.Background,
                            new Action(() =>
                            {
                                assistantMessage.Content += contentToAdd;
                            })
                        );
                    }
                },
                usageInfo =>
                {
                    // Обновляем статистику использования
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        SessionPromptTokens += usageInfo.PromptTokens;
                        SessionCompletionTokens += usageInfo.CompletionTokens;

                        if (SelectedModel?.PromptPrice.HasValue == true &&
                            SelectedModel?.CompletionPrice.HasValue == true)
                        {
                            decimal promptCost = usageInfo.PromptTokens * SelectedModel.PromptPrice.Value / 1000000m;
                            decimal completionCost = usageInfo.CompletionTokens * SelectedModel.CompletionPrice.Value / 1000000m;
                            SessionCost += promptCost + completionCost;
                        }
                    });
                },
                cancellationTokenSource.Token
            );

            // Обновляем оставшиеся данные из буфера
            string remainingContent;
            lock (buffer)
            {
                remainingContent = buffer.ToString();
                _ = buffer.Clear();
            }

            if (!string.IsNullOrEmpty(remainingContent))
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    assistantMessage.Content += remainingContent;
                });
            }

            assistantMessage.IsStreaming = false;
            return assistantMessage;
        }

        /// <summary>
        /// Обрабатывает результат выполнения MCP команды и добавляет сообщение в чат
        /// </summary>
        /// <returns>True если нужно продолжить цикл (отправить результат AI), False если выход</returns>
        private bool ProcessMcpCommandResult(RpaOperationResult result)
        {
            try
            {
                Debug.WriteLine($"[MCP] ProcessResult: Success={result.Success}, ExecutedCommands={result.ExecutedCommands.Count}, Errors={result.Errors.Count}");

                // Если нет команд и нет ошибок - выходим
                if (result.ExecutedCommands.Count == 0 && result.Errors.Count == 0)
                {
                    return false;
                }

                // Создаем сообщение с результатом
                ChatMessage resultMessage = new ChatMessage
                {
                    Role = "user",
                    Content = result.Success
                        ? $"✅ Результат выполнения команды:\n{string.Join("\n", result.ExecutedCommands)}"
                        : $"❌ {result.Message}\n{string.Join("\n", result.Errors)}",
                    Timestamp = DateTime.Now,
                    Type = MessageType.McpCommand
                };

                Messages.Add(resultMessage);
                CurrentConversation.Messages.Add(resultMessage);

                Debug.WriteLine($"[MCP] Added result message to chat");
                return true; // Продолжаем цикл - отправляем результат AI
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MCP-ERROR] ProcessMcpCommandResult exception: {ex.Message}");

                // Добавляем сообщение об ошибке
                ChatMessage errorMessage = new ChatMessage
                {
                    Role = "system",
                    Content = $"❌ Ошибка обработки результата команды: {ex.Message}",
                    Timestamp = DateTime.Now,
                    Type = MessageType.McpCommand
                };
                Messages.Add(errorMessage);
                CurrentConversation.Messages.Add(errorMessage);

                return false; // Прерываем цикл при критической ошибке
            }
        }

        /// <summary>
        /// Проверяет, можно ли отправить сообщение
        /// </summary>
        private bool CanSendMessage()
        {
            return !IsSending && !string.IsNullOrWhiteSpace(UserInput) && SelectedModel != null;
        }

        /// <summary>
        /// Отменяет генерацию ответа
        /// </summary>
        private void CancelGeneration()
        {
            _cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// Очищает чат
        /// </summary>
        private void ClearChat()
        {
            Messages.Clear();
            CurrentConversation = new Conversation();
            if (SelectedModel != null)
            {
                CurrentConversation.ModelName = SelectedModel.Name;
            }

            // Сбрасываем статистику сеанса
            SessionPromptTokens = 0;
            SessionCompletionTokens = 0;
            SessionCost = 0;

            StatusMessage = "Чат очищен";
        }

        /// <summary>
        /// Инициализирует провайдер по умолчанию
        /// </summary>
        private async Task InitializeDefaultProviderAsync()
        {
            try
            {
                IAiProvider defaultProvider = await _providerFactory.GetDefaultProviderAsync();
                _currentProvider = defaultProvider;
                _selectedProvider = defaultProvider.Type;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    OnPropertyChanged(nameof(SelectedProvider));
                });

                await LoadModelsAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка инициализации провайдера: {ex.Message}";
            }
        }

        /// <summary>
        /// Загружает список доступных моделей от текущего провайдера
        /// </summary>
        private async Task LoadModelsAsync()
        {
            if (_currentProvider == null)
            {
                StatusMessage = "Провайдер не выбран";
                return;
            }

            StatusMessage = $"Загрузка моделей от {_currentProvider.DisplayName}...";

            try
            {
                // Проверяем, доступен ли провайдер
                bool isAvailable = await _currentProvider.IsAvailableAsync();
                if (!isAvailable)
                {
                    string providerName = _currentProvider.DisplayName;
                    string errorMessage = _currentProvider.Type == ProviderType.Ollama
                        ? "Не удалось подключиться к Ollama.\nУбедитесь, что Ollama запущен (http://localhost:11434)"
                        : $"Провайдер {providerName} недоступен.\nПроверьте настройки API ключа.";

                    StatusMessage = $"{providerName} недоступен";
                    _ = MessageBox.Show(
                        errorMessage,
                        $"{providerName} недоступен",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }

                System.Collections.Generic.List<AiModel> models = await _currentProvider.GetModelsAsync();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableModels.Clear();
                    ModelDisplayNames.Clear();
                    foreach (AiModel model in models)
                    {
                        AvailableModels.Add(model);
                        ModelDisplayNames.Add(model.DisplayName);
                    }

                    // Загружаем сохраненную модель для текущего провайдера или выбираем первую
                    if (AvailableModels.Count > 0)
                    {
                        // Получаем сохраненную модель для текущего провайдера
                        string savedModelId = GetSavedModelIdForProvider(SelectedProvider);
                        AiModel modelToSelect = null;

                        if (!string.IsNullOrEmpty(savedModelId))
                        {
                            // Пытаемся найти сохраненную модель в списке доступных
                            modelToSelect = AvailableModels.FirstOrDefault(m => m.Id == savedModelId);
                        }

                        // Если сохраненная модель не найдена, выбираем первую доступную
                        modelToSelect ??= AvailableModels[0];

                        SelectedModel = modelToSelect;
                    }

                    StatusMessage = string.Format("Загружено {0} моделей от {1}", models.Count, _currentProvider.DisplayName);
                });
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format("Ошибка загрузки моделей: {0}", ex.Message);
                _ = MessageBox.Show(
                    string.Format("Не удалось загрузить модели от {0}:\n{1}", _currentProvider.DisplayName, ex.Message),
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        /// <summary>
        /// Копирует весь разговор в буфер обмена с форматированием
        /// </summary>
        private void CopyConversationToClipboard()
        {
            try
            {
                if (Messages.Count == 0)
                {
                    StatusMessage = "Нет сообщений для копирования";
                    return;
                }

                StringBuilder conversationText = new StringBuilder();
                _ = conversationText.AppendLine("═══════════════════════════════════════════════");
                _ = conversationText.AppendLine($"          ИСТОРИЯ ЧАТА");
                _ = conversationText.AppendLine($"   {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
                _ = conversationText.AppendLine("═══════════════════════════════════════════════");
                _ = conversationText.AppendLine();

                foreach (ChatMessage message in Messages)
                {
                    // Определяем роль отправителя
                    string roleLabel = message.Role switch
                    {
                        "user" => message.Type == MessageType.McpCommand ? "🔧 [MCP КОМАНДА]" : "👤 [ПОЛЬЗОВАТЕЛЬ]",
                        "assistant" => "🤖 [AI-АГЕНТ]",
                        "system" => "⚙️ [СИСТЕМА]",
                        _ => $"[{message.Role.ToUpper()}]"
                    };

                    // Добавляем разделитель
                    _ = conversationText.AppendLine("───────────────────────────────────────────────");

                    // Добавляем заголовок сообщения
                    _ = conversationText.AppendLine($"{roleLabel} [{message.Timestamp:HH:mm:ss}]");
                    _ = conversationText.AppendLine();

                    // Добавляем содержимое сообщения
                    if (!string.IsNullOrWhiteSpace(message.Content))
                    {
                        _ = conversationText.AppendLine(message.Content.Trim());
                    }

                    _ = conversationText.AppendLine();
                }

                _ = conversationText.AppendLine("═══════════════════════════════════════════════");
                _ = conversationText.AppendLine($"           КОНЕЦ ЧАТА");
                _ = conversationText.AppendLine($"   Всего сообщений: {Messages.Count}");
                _ = conversationText.AppendLine("═══════════════════════════════════════════════");

                // Копируем в буфер обмена
                Clipboard.SetText(conversationText.ToString());
                StatusMessage = $"Чат скопирован в буфер обмена ({Messages.Count} сообщений)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка копирования: {ex.Message}";
                _ = MessageBox.Show(
                    $"Не удалось скопировать чат:\n{ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        /// <summary>
        /// Сохраняет текущий диалог
        /// </summary>
        private async Task SaveConversationAsync()
        {
            try
            {
                CurrentConversation.UpdatedAt = DateTime.Now;

                // Генерируем название на основе первого сообщения
                if (CurrentConversation.Title == "Новый диалог" && CurrentConversation.Messages.Count > 0)
                {
                    ChatMessage firstMessage = CurrentConversation.Messages.FirstOrDefault(m => m.Role == "user");
                    if (firstMessage != null)
                    {
                        CurrentConversation.Title = firstMessage.Content.Length > 50
                            ? firstMessage.Content.Substring(0, 47) + "..."
                            : firstMessage.Content;
                    }
                }

                await _conversationStorage.SaveConversationAsync(CurrentConversation);
                StatusMessage = "Диалог сохранен";
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format("Ошибка сохранения: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Показывает список доступных активностей
        /// </summary>
        private async Task ShowAvailableActivitiesAsync()
        {
            try
            {
                StatusMessage = "Загрузка списка активностей...";

                string context = await _rpaIntegration.GenerateAiContextAsync();

                ChatMessage systemMessage = new ChatMessage
                {
                    Role = "system",
                    Content = $"📋 Доступные активности:\n\n{context}",
                    Timestamp = DateTime.Now,
                    Type = MessageType.System
                };

                Messages.Add(systemMessage);
                StatusMessage = "Список активностей загружен";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка загрузки активностей: {ex.Message}";
                ChatMessage errorMessage = new ChatMessage
                {
                    Role = "system",
                    Content = $"❌ Не удалось загрузить список активностей: {ex.Message}",
                    Timestamp = DateTime.Now,
                    Type = MessageType.System
                };
                Messages.Add(errorMessage);
            }
        }

        /// <summary>
        /// Загружает список MCP инструментов и обновляет системный промпт
        /// </summary>
        private async Task LoadMcpToolsAsync()
        {
            try
            {
                string mcpContext = await _rpaIntegration.GenerateAiContextAsync();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Формируем полный системный промпт с описанием MCP инструментов
                    SystemPrompt = @"Вы - AI ассистент для RPA Studio, который помогает создавать и управлять автоматизированными рабочими процессами.

" + mcpContext + @"

Важные правила:
1. Когда пользователь просит создать или изменить workflow, используйте доступные MCP команды
2. Всегда оборачивайте команды в блоки ```json
3. Предоставляйте четкие объяснения того, что делает каждая команда
4. Если нужно выполнить несколько операций, создавайте отдельную команду для каждой
5. Отвечайте на русском языке";

                    OnPropertyChanged(nameof(SystemPrompt));
                });

                StatusMessage = "MCP инструменты загружены";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка загрузки MCP инструментов: {ex.Message}";
            }
        }

        /// <summary>
        /// Загружает настройки из Properties.Settings
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                OllamaUrl = Properties.Settings.Default.OllamaUrl;
                Temperature = Properties.Settings.Default.Temperature;
                int maxTokens = Properties.Settings.Default.MaxTokens;
                MaxTokens = maxTokens > 0 ? maxTokens : (int?)null;
                AutoSaveConversations = Properties.Settings.Default.AutoSaveConversations;
                ShowTokenStats = Properties.Settings.Default.ShowTokenStats;
                EnableSoundNotifications = Properties.Settings.Default.EnableSoundNotifications;

                // Загружаем настройки провайдеров
                LoadProviderSettings();
            }
            catch
            {
                // Игнорируем ошибки загрузки настроек, используем значения по умолчанию
            }
        }

        /// <summary>
        /// Загружает настройки провайдеров из JSON
        /// </summary>
        private void LoadProviderSettings()
        {
            try
            {
                string json = Properties.Settings.Default.AiProviderSettings;
                if (!string.IsNullOrEmpty(json))
                {
                    _allProviderSettings = JsonConvert.DeserializeObject<AllProviderSettings>(json);
                }
            }
            catch
            {
                // Если не удалось загрузить, создаем новый объект
            }

            // Если объект еще не создан, создаем новый
            _allProviderSettings ??= new AllProviderSettings();
        }

        /// <summary>
        /// Сохраняет настройки провайдеров в JSON
        /// </summary>
        private void SaveProviderSettings()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_allProviderSettings, Formatting.Indented);
                Properties.Settings.Default.AiProviderSettings = json;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка сохранения настроек провайдера: {ex.Message}";
            }
        }

        /// <summary>
        /// Сохраняет ID выбранной модели для текущего провайдера
        /// </summary>
        private void SaveSelectedModelForCurrentProvider()
        {
            if (SelectedModel != null && _allProviderSettings != null)
            {
                _allProviderSettings.SetSelectedModel(SelectedProvider, SelectedModel.Id);
                SaveProviderSettings();
            }
        }

        /// <summary>
        /// Получает ID сохраненной модели для указанного провайдера
        /// </summary>
        private string GetSavedModelIdForProvider(ProviderType providerType)
        {
            return _allProviderSettings?.GetSelectedModelId(providerType);
        }

        /// <summary>
        /// Переключает видимость панели настроек
        /// </summary>
        private void ToggleSettings()
        {
            IsSettingsVisible = !IsSettingsVisible;
        }

        /// <summary>
        /// Закрывает панель настроек
        /// </summary>
        private void CloseSettings()
        {
            IsSettingsVisible = false;
        }

        /// <summary>
        /// Сохраняет настройки
        /// </summary>
        private void SaveSettings()
        {
            try
            {
                Properties.Settings.Default.OllamaUrl = OllamaUrl;
                Properties.Settings.Default.Temperature = Temperature;
                Properties.Settings.Default.MaxTokens = MaxTokens ?? 0;
                Properties.Settings.Default.AutoSaveConversations = AutoSaveConversations;
                Properties.Settings.Default.ShowTokenStats = ShowTokenStats;
                Properties.Settings.Default.EnableSoundNotifications = EnableSoundNotifications;
                Properties.Settings.Default.Save();

                StatusMessage = "Настройки сохранены";
                IsSettingsVisible = false;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка сохранения настроек: {ex.Message}";
            }
        }

        /// <summary>
        /// Сбрасывает настройки к значениям по умолчанию
        /// </summary>
        private void ResetSettings()
        {
            OllamaUrl = "http://localhost:11434";
            Temperature = 0.7;
            MaxTokens = null;
            AutoSaveConversations = true;
            ShowTokenStats = true;
            EnableSoundNotifications = false;
            StatusMessage = "Настройки сброшены";
        }

        /// <summary>
        /// Тестирует подключение к Ollama
        /// </summary>
        private async Task TestOllamaConnectionAsync()
        {
            try
            {
                StatusMessage = "Проверка подключения к Ollama...";

                IAiProvider ollamaProvider = _providerFactory.GetProvider(ProviderType.Ollama);
                bool isAvailable = await ollamaProvider.IsAvailableAsync();

                if (isAvailable)
                {
                    StatusMessage = "✅ Ollama доступен";
                    _ = MessageBox.Show(
                        "Подключение к Ollama успешно установлено!",
                        "Успех",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
                else
                {
                    StatusMessage = "❌ Ollama недоступен";
                    _ = MessageBox.Show(
                        "Не удалось подключиться к Ollama.\nУбедитесь, что сервер запущен на " + OllamaUrl,
                        "Ошибка подключения",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка: {ex.Message}";
                _ = MessageBox.Show(
                    $"Ошибка при проверке подключения:\n{ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        /// <summary>
        /// Переключает видимость панели истории
        /// </summary>
        private void ToggleHistory()
        {
            IsHistoryVisible = !IsHistoryVisible;
            if (IsHistoryVisible)
            {
                _ = Task.Run(async () => await RefreshHistoryAsync());
            }
        }

        /// <summary>
        /// Закрывает панель истории
        /// </summary>
        private void CloseHistory()
        {
            IsHistoryVisible = false;
        }

        /// <summary>
        /// Загружает выбранный диалог
        /// </summary>
        private async Task LoadConversationAsync(Conversation conversation)
        {
            if (conversation == null)
            {
                return;
            }

            try
            {
                Stopwatch totalStopwatch = Stopwatch.StartNew();

                // Загружаем полный диалог из хранилища
                Stopwatch loadStopwatch = Stopwatch.StartNew();
                Conversation loadedConversation = await _conversationStorage.LoadConversationAsync(conversation.Id);
                loadStopwatch.Stop();

                if (loadedConversation != null)
                {

                    // Очищаем текущий чат
                    Stopwatch clearStopwatch = Stopwatch.StartNew();
                    Messages.Clear();
                    clearStopwatch.Stop();

                    // Загружаем сообщения из диалога
                    Stopwatch addStopwatch = Stopwatch.StartNew();
                    foreach (ChatMessage message in loadedConversation.Messages)
                    {
                        Messages.Add(message);
                    }
                    addStopwatch.Stop();

                    // Устанавливаем текущий диалог
                    CurrentConversation = loadedConversation;

                    // Сбрасываем статистику сеанса
                    SessionPromptTokens = 0;
                    SessionCompletionTokens = 0;
                    SessionCost = 0;

                    totalStopwatch.Stop();

                    StatusMessage = $"Диалог '{loadedConversation.Title}' загружен";
                    IsHistoryVisible = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] LoadConversation failed: {ex.Message}");
                StatusMessage = $"Ошибка загрузки диалога: {ex.Message}";
                _ = MessageBox.Show(
                    $"Не удалось загрузить диалог:\n{ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        /// <summary>
        /// Удаляет диалог из истории
        /// </summary>
        private async Task DeleteConversationAsync(Conversation conversation)
        {
            if (conversation == null)
            {
                return;
            }

            try
            {
                MessageBoxResult result = MessageBox.Show(
                    $"Вы уверены, что хотите удалить диалог '{conversation.Title}'?",
                    "Подтверждение удаления",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    await _conversationStorage.DeleteConversationAsync(conversation.Id);
                    await RefreshHistoryAsync();
                    StatusMessage = "Диалог удален";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка удаления диалога: {ex.Message}";
                _ = MessageBox.Show(
                    $"Не удалось удалить диалог:\n{ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        /// <summary>
        /// Обновляет список истории диалогов
        /// </summary>
        private async Task RefreshHistoryAsync()
        {
            try
            {
                System.Collections.Generic.List<Conversation> conversations = await _conversationStorage.GetAllConversationsAsync();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    ConversationHistory.Clear();
                    foreach (Conversation conv in conversations)
                    {
                        ConversationHistory.Add(conv);
                    }
                    OnPropertyChanged(nameof(HasConversationHistory));
                });

                StatusMessage = $"Загружено {conversations.Count} диалогов";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка загрузки истории: {ex.Message}";
            }
        }

        /// <summary>
        /// Копирует содержимое сообщения в буфер обмена
        /// </summary>
        private void CopyMessageToClipboard(ChatMessage message)
        {
            if (message == null || string.IsNullOrEmpty(message.Content))
            {
                return;
            }

            try
            {
                Clipboard.SetText(message.Content);
                StatusMessage = "Сообщение скопировано в буфер обмена";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка копирования: {ex.Message}";
            }
        }

        /// <summary>
        /// Переключает видимость панели MCP Auto-Approve
        /// </summary>
        private void ToggleMcpAutoApprove()
        {
            IsMcpAutoApproveVisible = !IsMcpAutoApproveVisible;
        }

        /// <summary>
        /// Закрывает панель MCP Auto-Approve
        /// </summary>
        public void CloseMcpAutoApprove()
        {
            IsMcpAutoApproveVisible = false;
            SaveMcpToolPermissions();
        }

        /// <summary>
        /// Выбирает все инструменты
        /// </summary>
        private void SelectAllTools()
        {
            foreach (McpToolPermission permission in McpToolPermissions)
            {
                permission.IsApproved = true;
            }
            OnPropertyChanged(nameof(ApprovedToolsCount));
        }

        /// <summary>
        /// Снимает выбор со всех инструментов
        /// </summary>
        private void SelectNoneTools()
        {
            foreach (McpToolPermission permission in McpToolPermissions)
            {
                permission.IsApproved = false;
            }
            OnPropertyChanged(nameof(ApprovedToolsCount));
        }

        /// <summary>
        /// Гарантирует, что разрешения MCP инструментов загружены (вызывается при первом показе контрола)
        /// </summary>
        public async Task EnsureMcpToolPermissionsLoadedAsync()
        {
            if (_mcpToolPermissionsLoaded)
            {
                return;
            }

            await LoadMcpToolPermissionsAsync();
        }

        /// <summary>
        /// Загружает разрешения MCP инструментов из настроек
        /// </summary>
        private async Task LoadMcpToolPermissionsAsync()
        {
            // Защита от параллельной загрузки
            if (_mcpToolPermissionsLoaded || _isLoadingMcpToolPermissions)
            {
                return;
            }

            _isLoadingMcpToolPermissions = true;

            try
            {
                // Список инструментов (создаем в фоновом потоке)
                System.Collections.Generic.List<string> toolNames = await Task.Run(() => new System.Collections.Generic.List<string>
                {
                    "add_activity", "list_activities", "get_activity_tree", "get_validation_errors",
                    "create_variable", "list_variables", "update_variable", "delete_variable",
                    "create_argument", "list_arguments", "get_activity_details", "find_activity",
                    "run_workflow", "debug_workflow", "stop_execution", "save_workflow", "list_files", "read_file",
                    "get_file_info", "search_files", "get_open_tabs", "add_namespace", "list_namespaces",
                    "add_assembly_reference", "list_assemblies", "create_from_template", "list_templates",
                    "extract_to_workflow", "inline_workflow", "analyze_workflow", "get_workflow_statistics",
                    "add_annotation", "list_annotations", "get_parent_activity", "get_child_activities",
                    "set_breakpoint", "remove_breakpoint", "list_breakpoints", "create_activity_file",
                    "copy_activity", "move_activity", "rename_activity", "remove_activity", "update_activity",
                    "save_as_template", "set_activity_property", "set_active_tab"
                });

                // Загружаем сохраненные разрешения (в фоне)
                McpPermissionsSettings savedSettings = await Task.Run(() =>
                {
                    string json = Properties.Settings.Default.McpToolPermissions;
                    McpPermissionsSettings settings = null;

                    if (!string.IsNullOrEmpty(json))
                    {
                        try
                        {
                            settings = JsonConvert.DeserializeObject<McpPermissionsSettings>(json);
                        }
                        catch
                        {
                            // Если не удалось десериализовать, создаем новый объект
                        }
                    }

                    return settings ?? new McpPermissionsSettings();
                });

                // Создаем permissions в фоновом потоке
                System.Collections.Generic.List<McpToolPermission> permissions = await Task.Run(() =>
                {
                    System.Collections.Generic.List<McpToolPermission> result = new System.Collections.Generic.List<McpToolPermission>();

                    foreach (string toolName in toolNames)
                    {
                        ToolPermissionData savedPermission = savedSettings.Permissions.FirstOrDefault(p => p.ToolName == toolName);

                        McpToolPermission permission = new McpToolPermission
                        {
                            ToolName = toolName,
                            DisplayName = GetToolDisplayName(toolName),
                            Category = GetToolCategory(toolName),
                            Icon = GetToolIcon(toolName),
                            Description = $"MCP tool: {toolName}",
                            IsApproved = savedPermission?.IsApproved ?? false
                        };

                        result.Add(permission);
                    }

                    return result;
                });

                // Обновляем UI асинхронно
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    McpToolPermissions.Clear();

                    // Добавляем permissions и подписываемся на события
                    foreach (McpToolPermission permission in permissions)
                    {
                        // Подписываемся на изменения разрешения
                        permission.PropertyChanged += (s, e) =>
                        {
                            if (e.PropertyName == nameof(McpToolPermission.IsApproved))
                            {
                                OnPropertyChanged(nameof(ApprovedToolsCount));
                                SaveMcpToolPermissions();
                            }
                        };

                        McpToolPermissions.Add(permission);
                    }

                    IsGlobalAutoApproveEnabled = savedSettings.GlobalEnabled;
                    OnPropertyChanged(nameof(ApprovedToolsCount));
                }, DispatcherPriority.Background);

                _mcpToolPermissionsLoaded = true;
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StatusMessage = $"Ошибка загрузки разрешений: {ex.Message}";
                });
            }
            finally
            {
                _isLoadingMcpToolPermissions = false;
            }
        }

        /// <summary>
        /// Сохраняет разрешения MCP инструментов в настройки
        /// </summary>
        private void SaveMcpToolPermissions()
        {
            try
            {
                McpPermissionsSettings settings = new McpPermissionsSettings
                {
                    GlobalEnabled = IsGlobalAutoApproveEnabled
                };

                foreach (McpToolPermission permission in McpToolPermissions)
                {
                    settings.Permissions.Add(new ToolPermissionData
                    {
                        ToolName = permission.ToolName,
                        IsApproved = permission.IsApproved
                    });
                }

                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                Properties.Settings.Default.McpToolPermissions = json;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка сохранения разрешений: {ex.Message}";
            }
        }

        /// <summary>
        /// Проверяет, одобрен ли инструмент для автоматического выполнения
        /// </summary>
        public bool IsToolApproved(string toolName)
        {
            if (!IsGlobalAutoApproveEnabled)
            {
                return false;
            }

            McpToolPermission permission = McpToolPermissions.FirstOrDefault(p => p.ToolName == toolName);
            return permission?.IsApproved ?? false;
        }

        /// <summary>
        /// Получает отображаемое имя для инструмента
        /// </summary>
        private string GetToolDisplayName(string toolName)
        {
            return toolName.Replace("_", " ").ToUpper();
        }

        /// <summary>
        /// Определяет категорию инструмента
        /// </summary>
        private string GetToolCategory(string toolName)
        {
            string[] readTools = { "list_activities", "get_activity_tree", "get_validation_errors", "list_files", "read_file", "get_file_info", "get_open_tabs", "list_variables", "list_arguments", "list_namespaces", "list_assemblies", "list_annotations", "list_breakpoints", "list_templates", "get_workflow_statistics", "search_files" };
            string[] writeTools = { "add_activity", "create_variable", "update_variable", "delete_variable", "create_argument", "add_namespace", "add_assembly_reference", "create_from_template", "add_annotation", "create_activity_file", "copy_activity", "move_activity", "rename_activity", "remove_activity", "update_activity", "save_as_template", "set_activity_property", "save_workflow", "extract_to_workflow", "inline_workflow" };
            string[] executeTools = { "run_workflow", "debug_workflow", "stop_execution" };
            string[] navigationTools = { "find_activity", "get_activity_details", "get_parent_activity", "get_child_activities", "set_active_tab", "analyze_workflow" };

            return readTools.Contains(toolName)
                ? "Read"
                : writeTools.Contains(toolName)
                ? "Write"
                : executeTools.Contains(toolName) ? "Execute" : navigationTools.Contains(toolName) ? "Navigate" : "MCP";
        }

        /// <summary>
        /// Получает иконку для инструмента
        /// </summary>
        private string GetToolIcon(string toolName)
        {
            return GetToolCategory(toolName) switch
            {
                "Read" => "Eye",
                "Write" => "Pencil",
                "Execute" => "Play",
                "Navigate" => "MapSearch",
                _ => "Tool"
            };
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
