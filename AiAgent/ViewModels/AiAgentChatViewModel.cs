using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AiAgent.Models;
using AiAgent.Services;

namespace AiAgent.ViewModels
{
    /// <summary>
    /// ViewModel для контрола чата с AI
    /// </summary>
    public class AiAgentChatViewModel : INotifyPropertyChanged
    {
        private readonly IOllamaService _ollamaService;
        private readonly IConversationStorage _conversationStorage;
        private CancellationTokenSource _cancellationTokenSource;

        private string _userInput;
        private bool _isSending;
        private OllamaModel _selectedModel;
        private string _statusMessage;

        public AiAgentChatViewModel(IOllamaService ollamaService, IConversationStorage conversationStorage)
        {
            _ollamaService = ollamaService ?? throw new ArgumentNullException(nameof(ollamaService));
            _conversationStorage = conversationStorage ?? throw new ArgumentNullException(nameof(conversationStorage));

            Messages = new ObservableCollection<ChatMessage>();
            AvailableModels = new ObservableCollection<OllamaModel>();
            CurrentConversation = new Conversation();

            // Инициализация команд
            SendMessageCommand = new RelayCommand(async _ => await SendMessageAsync(), _ => CanSendMessage());
            ClearChatCommand = new RelayCommand(_ => ClearChat(), _ => Messages.Count > 0);
            LoadModelsCommand = new RelayCommand(async _ => await LoadModelsAsync());
            SaveConversationCommand = new RelayCommand(async _ => await SaveConversationAsync(), _ => Messages.Count > 0);
            CancelGenerationCommand = new RelayCommand(_ => CancelGeneration(), _ => IsSending);

            // Загружаем модели при инициализации
            Task.Run(async () => await LoadModelsAsync());
        }

        #region Properties

        /// <summary>
        /// Коллекция сообщений в чате
        /// </summary>
        public ObservableCollection<ChatMessage> Messages { get; }

        /// <summary>
        /// Доступные модели Ollama
        /// </summary>
        public ObservableCollection<OllamaModel> AvailableModels { get; }

        /// <summary>
        /// Текущий диалог
        /// </summary>
        public Conversation CurrentConversation { get; set; }

        /// <summary>
        /// Текст ввода пользователя
        /// </summary>
        public string UserInput
        {
            get { return _userInput; }
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
            get { return _isSending; }
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
        public OllamaModel SelectedModel
        {
            get { return _selectedModel; }
            set
            {
                if (_selectedModel != value)
                {
                    _selectedModel = value;
                    OnPropertyChanged(nameof(SelectedModel));
                    if (value != null)
                    {
                        CurrentConversation.ModelName = value.Name;
                    }
                }
            }
        }

        /// <summary>
        /// Сообщение статуса
        /// </summary>
        public string StatusMessage
        {
            get { return _statusMessage; }
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged(nameof(StatusMessage));
                }
            }
        }

        #endregion

        #region Commands

        public ICommand SendMessageCommand { get; }
        public ICommand ClearChatCommand { get; }
        public ICommand LoadModelsCommand { get; }
        public ICommand SaveConversationCommand { get; }
        public ICommand CancelGenerationCommand { get; }

        #endregion

        #region Methods

        /// <summary>
        /// Отправляет сообщение пользователя и получает ответ от AI
        /// </summary>
        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(UserInput))
                return;

            if (SelectedModel == null)
            {
                StatusMessage = "Выберите модель для чата";
                return;
            }

            var userMessage = new ChatMessage
            {
                Role = "user",
                Content = UserInput,
                Timestamp = DateTime.Now
            };

            // Добавляем сообщение пользователя
            Messages.Add(userMessage);
            CurrentConversation.Messages.Add(userMessage);

            var inputText = UserInput;
            UserInput = string.Empty;

            IsSending = true;
            StatusMessage = "Отправка сообщения...";

            try
            {
                // Создаем сообщение для ответа AI
                var assistantMessage = new ChatMessage
                {
                    Role = "assistant",
                    Content = string.Empty,
                    Timestamp = DateTime.Now,
                    IsStreaming = true
                };

                Messages.Add(assistantMessage);

                _cancellationTokenSource = new CancellationTokenSource();

                // Буфер для накопления чанков
                var buffer = new StringBuilder();
                var lastUpdateTime = DateTime.Now;
                var updateInterval = TimeSpan.FromMilliseconds(50); // Обновляем UI не чаще чем раз в 50мс

                // Получаем ответ с потоковой передачей
                await _ollamaService.ChatStreamAsync(
                    SelectedModel.Name,
                    Messages.Where(m => !m.IsStreaming).ToList(),
                    chunk =>
                    {
                        lock (buffer)
                        {
                            buffer.Append(chunk);
                        }

                        // Обновляем UI асинхронно с интервалом
                        var timeSinceLastUpdate = DateTime.Now - lastUpdateTime;
                        if (timeSinceLastUpdate >= updateInterval)
                        {
                            lastUpdateTime = DateTime.Now;
                            string contentToAdd;
                            lock (buffer)
                            {
                                contentToAdd = buffer.ToString();
                                buffer.Clear();
                            }

                            // Используем BeginInvoke для асинхронного обновления UI
                            Application.Current.Dispatcher.BeginInvoke(
                                DispatcherPriority.Background,
                                new Action(() =>
                                {
                                    assistantMessage.Content += contentToAdd;
                                })
                            );
                        }
                    },
                    _cancellationTokenSource.Token
                );

                // Обновляем оставшиеся данные из буфера
                string remainingContent;
                lock (buffer)
                {
                    remainingContent = buffer.ToString();
                    buffer.Clear();
                }
                
                if (!string.IsNullOrEmpty(remainingContent))
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        assistantMessage.Content += remainingContent;
                    });
                }

                assistantMessage.IsStreaming = false;
                CurrentConversation.Messages.Add(assistantMessage);

                StatusMessage = "Ответ получен";

                // Автосохранение
                await SaveConversationAsync();
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Генерация отменена";
                // Удаляем незавершенное сообщение
                if (Messages.Count > 0 && Messages[Messages.Count - 1].IsStreaming)
                {
                    Messages.RemoveAt(Messages.Count - 1);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format("Ошибка: {0}", ex.Message);
                MessageBox.Show(
                    string.Format("Не удалось получить ответ от Ollama:\n{0}", ex.Message),
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                // Удаляем сообщение с ошибкой
                if (Messages.Count > 0 && Messages[Messages.Count - 1].Role == "assistant")
                {
                    Messages.RemoveAt(Messages.Count - 1);
                }
            }
            finally
            {
                IsSending = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
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
            StatusMessage = "Чат очищен";
        }

        /// <summary>
        /// Загружает список доступных моделей
        /// </summary>
        private async Task LoadModelsAsync()
        {
            StatusMessage = "Загрузка моделей...";
            
            try
            {
                // Проверяем, запущен ли Ollama
                var isRunning = await _ollamaService.IsOllamaRunningAsync();
                if (!isRunning)
                {
                    StatusMessage = "Ollama не запущен. Запустите Ollama и обновите список моделей.";
                    MessageBox.Show(
                        "Не удалось подключиться к Ollama.\nУбедитесь, что Ollama запущен (http://localhost:11434)",
                        "Ollama недоступен",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }

                var models = await _ollamaService.GetModelsAsync();
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableModels.Clear();
                    foreach (var model in models)
                    {
                        AvailableModels.Add(model);
                    }

                    if (AvailableModels.Count > 0 && SelectedModel == null)
                    {
                        SelectedModel = AvailableModels[0];
                    }

                    StatusMessage = string.Format("Загружено {0} моделей", models.Count);
                });
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format("Ошибка загрузки моделей: {0}", ex.Message);
                MessageBox.Show(
                    string.Format("Не удалось загрузить модели:\n{0}", ex.Message),
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
                    var firstMessage = CurrentConversation.Messages.FirstOrDefault(m => m.Role == "user");
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