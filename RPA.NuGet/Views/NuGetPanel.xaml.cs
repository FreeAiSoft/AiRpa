using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using RPA.NuGet.ViewModels;

namespace RPA.NuGet.Views
{
    /// <summary>
    /// Докируемая панель менеджера пакетов NuGet (вкладки Browse/Installed/Updates, поиск,
    /// панель деталей с выбором версии). Открывается вкладкой панели документов, но остаётся
    /// переносимой: в узком боковом доке детали показываются вместо списка.
    /// <para>
    /// Здесь только представление: горячие клавиши, раскладка при изменении ширины, открытие
    /// внешних ссылок и выбор файла проекта. Вся работа с пакетами — в
    /// <see cref="NuGetPanelViewModel"/>.
    /// </para>
    /// </summary>
    public partial class NuGetPanel : UserControl
    {
        /// <summary>Порог «узкой панели»: ниже него список и детали не помещаются рядом.</summary>
        private const double CompactWidthThreshold = 640d;

        private readonly NuGetPanelViewModel _viewModel;

        public NuGetPanel()
            : this(new NuGetPanelViewModel())
        {
        }

        /// <summary>
        /// Панель с готовой моделью представления. Нужен тестам: там модель создаётся на заведомо
        /// недоступном источнике, и разметка проверяется без обращений к сети.
        /// </summary>
        internal NuGetPanel(NuGetPanelViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? new NuGetPanelViewModel();
            DataContext = _viewModel;

            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            _viewModel.LinkRequested += ViewModel_LinkRequested;
            Loaded += NuGetPanel_Loaded;
            Unloaded += NuGetPanel_Unloaded;
        }

        /// <summary>Модель представления панели (для хоста: заголовок, состояние).</summary>
        public NuGetPanelViewModel ViewModel => _viewModel;

        /// <summary>Sets the panel's project from a .csproj path.</summary>
        public bool SetProject(string projectPath)
        {
            return _viewModel.LoadProject(projectPath);
        }

        /// <summary>Auto-detects a .csproj inside the given folder.</summary>
        public bool SetProjectFolder(string folder)
        {
            return _viewModel.TryDetectProject(folder);
        }

        private void NuGetPanel_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyLayout(ActualWidth);
            SearchTextBox?.Focus();
        }

        private void NuGetPanel_Unloaded(object sender, RoutedEventArgs e)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel.LinkRequested -= ViewModel_LinkRequested;
        }

        private void Panel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyLayout(e.NewSize.Width);
        }

        /// <summary>
        /// Раскладка «список | 1px | детали»: при узкой панели детали занимают всё место, а список
        /// скрывается (кнопка «назад» возвращает к нему) — второй шаблон разметки не нужен.
        /// </summary>
        private void ApplyLayout(double width)
        {
            if (ListColumn == null || DetailsColumn == null || SplitterColumn == null)
                return;

            bool compact = width > 0 && width < CompactWidthThreshold;
            _viewModel.IsCompact = compact;

            // Пока пакет не выбран, правой колонки нет вовсе: список занимает всю ширину.
            bool showDetails = _viewModel.HasSelection;

            ListColumn.Width = compact && showDetails
                ? new GridLength(0)
                : new GridLength(1, GridUnitType.Star);
            SplitterColumn.Width = compact || !showDetails ? new GridLength(0) : new GridLength(1);
            DetailsColumn.Width = showDetails
                ? new GridLength(compact ? 1 : 0.65, GridUnitType.Star)
                : new GridLength(0);

            if (Splitter != null)
                Splitter.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;

            if (ListPanel != null)
                ListPanel.Visibility = compact && showDetails ? Visibility.Collapsed : Visibility.Visible;

            if (DetailsPanel != null)
                DetailsPanel.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;

            if (BackButton != null)
                BackButton.Visibility = compact && _viewModel.HasSelection ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NuGetPanelViewModel.HasSelection))
                ApplyLayout(ActualWidth);
        }

        private void Panel_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.L && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                SearchTextBox?.Focus();
                SearchTextBox?.SelectAll();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                _viewModel.ClearSearch();
                e.Handled = true;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SelectedItem = null;
        }

        /// <summary>Открывает ссылку (Project URL, лицензия, NuGet.Config) средствами оболочки.</summary>
        private void ViewModel_LinkRequested(object sender, string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // Нет обработчика ссылки/файл недоступен — панель не должна падать.
            }
        }

        /// <summary>Выбор файла проекта вручную (когда автопоиск .csproj не сработал).</summary>
        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select project",
                Filter = "Project files (*.csproj)|*.csproj|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
                _viewModel.LoadProject(dialog.FileName);
        }
    }
}
