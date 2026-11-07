using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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

        /// <summary>Автофокус в поиск уже выполнен: повторный Loaded фокус пользователя не перебивает.</summary>
        private bool _searchFocusApplied;

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

        /// <summary>
        /// Панель загрузилась. Фокус ставится <b>не здесь</b>, а на следующем цикле диспетчера:
        /// <c>Loaded</c> приходит синхронно из <c>HwndTarget.OnResize</c> (WM_SIZE — разворачивание
        /// окна, восстановление раскладки доков), а синхронный <c>Focus()</c> в этот момент падает
        /// <c>NullReferenceException</c> в <c>HwndKeyboardInputProvider.AcquireFocus</c>: провайдер
        /// ввода ещё не привязан к HwndSource. Отсюда <see cref="FocusSearchBoxDeferred"/>.
        /// </summary>
        private void NuGetPanel_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyLayout(ActualWidth);
            FocusSearchBoxDeferred();
        }

        /// <summary>
        /// Отложенная установка фокуса в поиск. Автофокус выполняется только один раз за жизнь
        /// панели: при переезде вкладки между панелью документов и боковым доком (Unloaded →
        /// Loaded) фокус пользователя перебивать нельзя.
        /// </summary>
        private void FocusSearchBoxDeferred()
        {
            if (_searchFocusApplied)
                return;

            TextBox search = SearchTextBox;
            if (search == null)
                return;

            _ = search.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => TryFocusSearchBox()));
        }

        /// <summary>
        /// Ставит фокус в поле поиска, если это безопасно: панель загружена и видима, у поля есть
        /// живой <see cref="PresentationSource"/> (без него провайдеру ввода нечего захватывать и
        /// Focus() падает), фокус ещё не внутри панели. Возвращает true, если поле поиска в фокусе.
        /// </summary>
        private bool TryFocusSearchBox()
        {
            TextBox search = SearchTextBox;
            if (search == null || !IsLoaded || !search.IsVisible)
                return false;

            // Нет HwndSource — Focus() уронил бы студию в HwndKeyboardInputProvider.AcquireFocus.
            if (PresentationSource.FromVisual(search) == null)
                return false;

            if (search.IsKeyboardFocused)
            {
                _searchFocusApplied = true;
                return true;
            }

            if (IsKeyboardFocusWithin)
                return false;

            try
            {
                bool focused = search.Focus();
                if (focused)
                    _searchFocusApplied = true;

                return focused;
            }
            catch (Exception)
            {
                // Окно пересоздаёт HwndSource (переезд вкладки, смена доков) — панель не падает.
                return false;
            }
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
                // Та же защищённая точка, что и у автофокуса: синхронный Focus() здесь тоже падал.
                if (TryFocusSearchBox())
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
