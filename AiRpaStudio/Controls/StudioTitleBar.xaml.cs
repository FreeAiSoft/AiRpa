using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RPA.Controls
{
    /// <summary>
    /// Полоса заголовка окна студии — тот же заголовок, что у главного окна
    /// (<c>Views\MainView.xaml</c>): высота 32, фон <c>Studio.TitleBarBackground</c>, кнопки
    /// «свернуть»/«закрыть» 46×32 на <c>MaterialIcon</c>, перетаскивание окна за полосу.
    /// <para>
    /// Появилась потому, что диалоги студии рисовали системный caption Windows: он белый в любой
    /// теме и не скругляется вместе с окном на Windows 10 (region-fallback
    /// <see cref="Helpers.WindowCorner.ApplyCornerRadiusFallback"/> работает только при
    /// наличии <c>WindowChrome</c>, а окну с системным заголовком chrome навязывать нельзя).
    /// </para>
    /// <para>
    /// Окно-владелец ищется через <see cref="Window.GetWindow"/>: контрол не знает, в какое окно его
    /// положили, и не требует DataContext. Все обработчики defensive — окно ошибки открывается из
    /// обработчика падения приложения, где исключение из UI-кода уже никто не покажет.
    /// </para>
    /// </summary>
    public partial class StudioTitleBar : UserControl
    {
        /// <summary>Показывать кнопку «свернуть». У окна ошибки она выключена: оно не в панели задач.</summary>
        public static readonly DependencyProperty ShowMinimizeProperty =
            DependencyProperty.Register(
                nameof(ShowMinimize),
                typeof(bool),
                typeof(StudioTitleBar),
                new PropertyMetadata(true));

        /// <summary>Показывать кнопку «закрыть».</summary>
        public static readonly DependencyProperty ShowCloseProperty =
            DependencyProperty.Register(
                nameof(ShowClose),
                typeof(bool),
                typeof(StudioTitleBar),
                new PropertyMetadata(true));

        public StudioTitleBar()
        {
            InitializeComponent();
        }

        /// <summary>Показывать кнопку «свернуть» (по умолчанию — да).</summary>
        public bool ShowMinimize
        {
            get => (bool)GetValue(ShowMinimizeProperty);
            set => SetValue(ShowMinimizeProperty, value);
        }

        /// <summary>Показывать кнопку «закрыть» (по умолчанию — да).</summary>
        public bool ShowClose
        {
            get => (bool)GetValue(ShowCloseProperty);
            set => SetValue(ShowCloseProperty, value);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Window window = Window.GetWindow(this);
            if (window == null)
            {
                return;
            }

            // Двойной клик по заголовку диалоги не разворачивает: они фиксированного размера.
            if (e.ClickCount == 2 || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            // DragMove в развёрнутом (и максимизированном) состоянии бросает InvalidOperationException.
            if (window.WindowState == WindowState.Maximized)
            {
                return;
            }

            try
            {
                window.DragMove();
            }
            catch (InvalidOperationException)
            {
                // Кнопка уже отпущена или окно потеряло фокус — перетаскивание просто не начинается.
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            Window window = Window.GetWindow(this);
            if (window == null)
            {
                return;
            }

            window.WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this)?.Close();
        }
    }
}
