using System;

namespace AvalonDock.Themes
{
    /// <summary>
    /// Мост скругления окон в ядро AvalonDock.
    /// <para>
    /// <see cref="AvalonDock.Controls.LayoutFloatingWindowControl"/> (откреплённые панели и документы) и
    /// <see cref="AvalonDock.Controls.NavigatorWindow"/> (Ctrl+Tab) живут в этой сборке, а сборка студии
    /// ссылается на неё, но не наоборот. Поэтому окна зовут мост, а конкретный механизм скругления
    /// (DWM <c>DWMWA_WINDOW_CORNER_PREFERENCE</c> с fallback на region) подставляет студия через
    /// <see cref="Provider"/> — зеркально <see cref="ThemeBrushBridge"/> для кистей.
    /// </para>
    /// <para>
    /// Без провайдера мост — no-op: ядро AvalonDock остаётся самостоятельным и работоспособным.
    /// </para>
    /// </summary>
    public static class WindowCornerBridge
    {
        /// <summary>
        /// Механизм скругления, который подставляет студия: HWND → <c>true</c>, если окно скруглено.
        /// </summary>
        public static Func<IntPtr, bool> Provider { get; set; }

        /// <summary>Подключён ли провайдер скругления.</summary>
        public static bool IsConnected => Provider != null;

        /// <summary>
        /// Скругляет окно по хэндлу. Возвращает <c>false</c>, если провайдер не подключён, хэндл пуст
        /// или провайдер завершился ошибкой; исключения наружу не выходят.
        /// </summary>
        public static bool Apply(IntPtr hwnd)
        {
            Func<IntPtr, bool> provider = Provider;
            if (provider == null || hwnd == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                return provider(hwnd);
            }
            catch (Exception)
            {
                // Скругление декоративно: сбой провайдера не должен мешать открытию окна.
                return false;
            }
        }

        /// <summary>Отключает провайдер (используется в тестах и при выгрузке приложения).</summary>
        public static void Disconnect() => Provider = null;
    }
}
