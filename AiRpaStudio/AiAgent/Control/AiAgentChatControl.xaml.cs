using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;
using RPA.AiAgent.Prompts;
using RPA.AiAgent.Services;
using RPA.AiAgent.ViewModels;
using RPA.Mcp;

namespace RPA.AiAgent.Control
{
    /// <summary>
    /// Interaction logic for AiAgentChatControl.xaml
    /// </summary>
    public partial class AiAgentChatControl : UserControl
    {
        public AiAgentChatControl()
        {
            Stopwatch initStopwatch = Stopwatch.StartNew();

            InitializeComponent();

            // Создаем фабрику провайдеров
            AiProviderFactory providerFactory = new AiProviderFactory();
            JsonConversationStorage conversationStorage = new JsonConversationStorage();

            // Создаем MCP инфраструктуру
            RpaMcpServer mcpServer = new RpaMcpServer();
            ActivityCommandParser commandParser = new ActivityCommandParser();
            RpaAiIntegrationService rpaIntegration = new RpaAiIntegrationService(commandParser, mcpServer);

            // Создаем ViewModel с фабрикой провайдеров
            AiAgentChatViewModel viewModel = new AiAgentChatViewModel(
                providerFactory,
                conversationStorage,
                rpaIntegration
            )
            {
                // Устанавливаем системный промпт
                SystemPrompt = ActivityPrompts.GetSystemPrompt()
            };

            DataContext = viewModel;

            // Автоматическая прокрутка вниз при добавлении новых сообщений (с защитой от ошибок)
            viewModel.Messages.CollectionChanged += (s, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    _ = Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        SafeScrollToEnd();
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            };

            // Фокус на поле ввода при загрузке (с защитой от ошибок при drag-and-drop)
            Loaded += (s, e) =>
            {
                SafeSetFocus();
            };

            initStopwatch.Stop();
        }

        private void SafeScrollToEnd()
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                // Проверяем, что ScrollViewer доступен и контрол подключен к визуальному дереву
                if (IsLoaded && IsVisible && MessageScrollViewer != null && MessageScrollViewer.IsVisible)
                {
                    MessageScrollViewer.ScrollToEnd();
                    sw.Stop();
                }
                else
                {
                    sw.Stop();
                }
            }
            catch
            {
                sw.Stop();
                // Игнорируем ошибки прокрутки при drag-and-drop операциях
            }
        }

        private void SafeSetFocus()
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                // Проверяем, что контрол подключен к визуальному дереву
                if (IsLoaded && IsVisible && InputTextBox != null)
                {
                    // Используем Dispatcher для асинхронной установки фокуса
                    _ = Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        Stopwatch focusSw = Stopwatch.StartNew();
                        try
                        {
                            if (InputTextBox.IsVisible && InputTextBox.IsEnabled)
                            {
                                bool focused = InputTextBox.Focus();
                                focusSw.Stop();
                            }
                            else
                            {
                                focusSw.Stop();
                            }
                        }
                        catch
                        {
                            focusSw.Stop();
                            // Игнорируем ошибки фокуса при drag-and-drop операциях
                        }
                    }), System.Windows.Threading.DispatcherPriority.Input);
                    sw.Stop();
                }
                else
                {
                    sw.Stop();
                }
            }
            catch
            {
                sw.Stop();
                // Игнорируем ошибки при установке фокуса во время перетаскивания
            }
        }

        private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Shift+Enter - новая строка (стандартное поведение)
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    return;
                }

                // Enter без модификаторов - отправка сообщения
                e.Handled = true;
                AiAgentChatViewModel viewModel = DataContext as AiAgentChatViewModel;
                if (viewModel?.SendMessageCommand.CanExecute(null) == true)
                {
                    viewModel.SendMessageCommand.Execute(null);
                }
            }
        }

        private void PopupBackground_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Закрываем панель MCP Auto-Approve при клике на затемняющий фон
            if (DataContext is AiAgentChatViewModel viewModel && viewModel.IsMcpAutoApproveVisible)
            {
                viewModel.CloseMcpAutoApprove();
            }
        }
    }
}
