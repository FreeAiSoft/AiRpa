using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using RPA.AiAgent.ViewModels;

namespace RPA.AiAgent.Control
{
    /// <summary>
    /// Interaction logic for McpAutoApproveControl.xaml
    /// </summary>
    public partial class McpAutoApproveControl : UserControl
    {
        private bool _isInitialized;

        public McpAutoApproveControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Асинхронная загрузка при первом отображении контрола
        /// </summary>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;

            // Запускаем загрузку полностью в фоне (fire-and-forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    // Показываем индикатор загрузки асинхронно
                    await Dispatcher.InvokeAsync(() =>
                    {
                        LoadingPanel.Visibility = Visibility.Visible;
                        ToolsScrollViewer.Visibility = Visibility.Collapsed;
                    }, System.Windows.Threading.DispatcherPriority.Loaded);

                    // Даем UI время на отрисовку и отклик
                    await Task.Delay(100);

                    // Загружаем данные асинхронно
                    if (DataContext is AiAgentChatViewModel viewModel)
                    {
                        await viewModel.EnsureMcpToolPermissionsLoadedAsync();
                    }

                    // Скрываем индикатор загрузки
                    await Dispatcher.InvokeAsync(() =>
                    {
                        LoadingPanel.Visibility = Visibility.Collapsed;
                        ToolsScrollViewer.Visibility = Visibility.Visible;
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    // В случае ошибки показываем контент без данных
                    await Dispatcher.InvokeAsync(() =>
                    {
                        LoadingPanel.Visibility = Visibility.Collapsed;
                        ToolsScrollViewer.Visibility = Visibility.Visible;
                    });

                    System.Diagnostics.Debug.WriteLine($"Error loading MCP tools: {ex.Message}");
                }
            });
        }
    }
}
