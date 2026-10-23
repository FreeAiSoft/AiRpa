using System.Windows.Controls;
using AiAgent.Services;
using AiAgent.ViewModels;

namespace AiAgent.Control
{
    /// <summary>
    /// Interaction logic for AiAgentChatControl.xaml
    /// </summary>
    public partial class AiAgentChatControl : UserControl
    {
        public AiAgentChatControl()
        {
            InitializeComponent();

            // Инициализация ViewModel с зависимостями
            var ollamaService = new OllamaService();
            var conversationStorage = new JsonConversationStorage();
            var viewModel = new AiAgentChatViewModel(ollamaService, conversationStorage);

            DataContext = viewModel;

            // Автоматическая прокрутка вниз при добавлении новых сообщений
            viewModel.Messages.CollectionChanged += (s, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        MessageScrollViewer.ScrollToEnd();
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            };

            // Фокус на поле ввода
            Loaded += (s, e) => InputTextBox.Focus();
        }
    }
}
