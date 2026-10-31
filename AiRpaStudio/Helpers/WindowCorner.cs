using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RPA.Helpers
{
    /// <summary>
    /// Скругление окон студии (план <c>docs\Vs2026_Theme_Plan.md</c>, §7 M-03, требования W-01…W-08).
    /// <para>
    /// Два механизма, как в Visual Studio 2026: основной — DWM
    /// (<c>DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND)</c>, Windows 11),
    /// fallback — region через <c>WindowChrome.CornerRadius</c> (Windows 10, см.
    /// <see cref="ApplyCornerRadiusFallback"/>). Радиус берётся только из <see cref="StudioPalette"/>,
    /// чтобы окна, вкладки и меню скруглялись согласованно.
    /// </para>
    /// <para>
    /// Хелпер декоративный: любой сбой P/Invoke (нет dwmapi, старый билд Windows, неизвестный атрибут,
    /// чужой HWND) возвращает <c>false</c> и никогда не бросает исключение — окно должно открыться в любом
    /// случае.
    /// </para>
    /// <para>
    /// Окна AvalonDock (откреплённые панели и документы, переключатель Ctrl+Tab) живут в отдельной сборке
    /// и не могут ссылаться на студию, поэтому они зовут <c>AvalonDock.Themes.WindowCornerBridge</c>,
    /// а провайдер в него ставит <see cref="EnableAvalonDockWindows"/>.
    /// </para>
    /// </summary>
    public static class WindowCorner
    {
        // Контракт dwmapi.dll: DWMWA_WINDOW_CORNER_PREFERENCE и значения DWM_WINDOW_CORNER_PREFERENCE.
        public const int DwmWindowCornerPreference = 33;
        public const int DwmWindowCornerDefault = 0;
        public const int DwmWindowCornerDoNotRound = 1;
        public const int DwmWindowCornerRound = 2;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>Состояние по окну; слабые ссылки — окна не удерживаются хелпером.</summary>
        private static readonly ConditionalWeakTable<Window, HookState> Hooked =
            new ConditionalWeakTable<Window, HookState>();

        private static bool _bridgeConnected;

        /// <summary>
        /// Просит DWM скруглить окно по <paramref name="hwnd"/>. Возвращает <c>false</c>, если скругление
        /// включить не удалось (Windows 10 и старше, невалидный HWND) — исключения не выходят наружу.
        /// </summary>
        public static bool Apply(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            int preference = DwmWindowCornerRound;
            try
            {
                return DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref preference, sizeof(int)) == 0;
            }
            catch (Exception)
            {
                // Нет dwmapi или точки входа: окно останется прямоугольным (region делает
                // ApplyCornerRadiusFallback), но интерфейс продолжает работать.
                return false;
            }
        }

        /// <summary>Скругляет окно по его HWND. Для окна без хэндла возвращает <c>false</c>.</summary>
        public static bool Apply(Window window)
        {
            if (window == null)
            {
                return false;
            }

            return Apply(new WindowInteropHelper(window).Handle);
        }

        /// <summary>
        /// Включает скругление окна: применяет DWM сразу, если хэндл уже создан, иначе — подпиской на
        /// <c>SourceInitialized</c>. Повторный вызов для того же окна ничего не делает (подписка одна).
        /// Возвращает <c>true</c>, если скругление применено или будет применено.
        /// </summary>
        public static bool Enable(Window window)
        {
            if (window == null)
            {
                return false;
            }

            HookState state = Hooked.GetOrCreateValue(window);
            if (state.Activations > 0)
            {
                return state.Applied;
            }

            state.Activations = 1;
            state.Applied = Apply(window);

            if (!state.Applied)
            {
                // Хэндла ещё нет (окно не показано) или Windows 10, где DWM-атрибут неизвестен.
                window.SourceInitialized += OnSourceInitialized;
            }

            return true;
        }

        /// <summary>Включено ли скругление для окна (для диагностики и тестов).</summary>
        public static bool IsEnabled(Window window)
        {
            return window != null && Hooked.TryGetValue(window, out _);
        }

        /// <summary>
        /// Сколько раз хелпер включал окно: 0 — не включено, 1 — включено.
        /// Значение больше единицы означало бы двойную подписку на SourceInitialized (для тестов и диагностики).
        /// </summary>
        public static int ActivationCount(Window window)
        {
            return window != null && Hooked.TryGetValue(window, out HookState state) ? state.Activations : 0;
        }

        /// <summary>
        /// Fallback Windows 10: region-скругление через <c>WindowChrome.CornerRadius</c>
        /// (<c>GlassFrameThickness = 0</c> включает путь <c>SetWindowRgn</c>). Работает только для окон,
        /// у которых chrome уже есть: навязывать его диалогу с системным заголовком нельзя — слетит caption.
        /// </summary>
        public static bool ApplyCornerRadiusFallback(Window window)
        {
            if (window == null)
            {
                return false;
            }

            System.Windows.Shell.WindowChrome chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(window);
            if (chrome == null)
            {
                return false;
            }

            chrome.CornerRadius = new CornerRadius(StudioPalette.WindowCornerRadius);
            chrome.GlassFrameThickness = new Thickness(0);
            return true;
        }

        /// <summary>
        /// Подключает скругление к окнам AvalonDock (откреплённые панели, документы, Ctrl+Tab).
        /// Вызывается один раз на старте приложения.
        /// </summary>
        public static void EnableAvalonDockWindows()
        {
            if (_bridgeConnected)
            {
                return;
            }

            AvalonDock.Themes.WindowCornerBridge.Provider = Apply;
            _bridgeConnected = true;
        }

        private static void OnSourceInitialized(object sender, EventArgs e)
        {
            if (!(sender is Window window) || !Hooked.TryGetValue(window, out HookState state))
            {
                return;
            }

            state.Applied = Apply(window);
            window.SourceInitialized -= OnSourceInitialized;
        }

        private sealed class HookState
        {
            public int Activations;
            public bool Applied;
        }
    }
}
