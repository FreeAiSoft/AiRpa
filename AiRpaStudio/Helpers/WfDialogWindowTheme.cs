using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using RPA.Controls;

namespace RPA.Helpers
{
    /// <summary>
    /// Тема для окон WF, которые создаёт не студия: редактор выражений по кнопке «…» в окне «Свойства»
    /// открывается в окне WF (<c>WorkflowElementDialogWindow</c>/<c>EditorDialog</c>), а это обычный
    /// <see cref="Window"/> с системным заголовком — он белый в любой теме, и <see cref="WfDesignerTheme"/>
    /// до него не достаёт (окно не является потомком <c>DesignerView</c>).
    /// <para>
    /// Что делает <see cref="Apply"/> (идемпотентно и без исключений):
    /// <list type="number">
    /// <item>снимает системный caption — <c>WindowChrome.CaptionHeight = 0</c> (как у окон студии);</item>
    /// <item>красит окно ключами палитры через <c>SetResourceReference</c> (переживает смену темы);</item>
    /// <item>оборачивает содержимое в <see cref="Grid"/> с полосой заголовка
    /// <see cref="StudioTitleBar"/> (32 px) — иначе окно нечем перетащить и нечем закрыть;</item>
    /// <item>включает скругление <see cref="WindowCorner"/> (DWM + region-fallback теперь работает,
    /// потому что chrome у окна появился).</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class WfDialogWindowTheme
    {
        /// <summary>Высота полосы заголовка — как у главного окна и диалогов студии.</summary>
        public const double TitleBarHeight = 32d;

        /// <summary>Толщина кромки для изменения размера: системный grip исчезает вместе с caption.</summary>
        public const double ResizeBorderThickness = 6d;

        private static readonly ConditionalWeakTable<Window, object> Applied = new ConditionalWeakTable<Window, object>();

        /// <summary>
        /// Применяет тему студии к окну WF. Возвращает <c>true</c>, если окно было изменено этим вызовом
        /// (повторный вызов для того же окна — <c>false</c>). Исключения не выходят наружу: окно должно
        /// открыться в любом случае.
        /// </summary>
        public static bool Apply(Window window)
        {
            if (window == null)
            {
                return false;
            }

            lock (Applied)
            {
                if (Applied.TryGetValue(window, out _))
                {
                    return false;
                }

                Applied.Add(window, new object());
            }

            try
            {
                ApplyWindowChrome(window);
                ApplyPalette(window);
                WrapContent(window);
                _ = WindowCorner.Enable(window);
                return true;
            }
            catch (Exception ex)
            {
                // Тема декоративная: диалог важнее темы.
                System.Diagnostics.Debug.WriteLine($"WfDialogWindowTheme.Apply ERROR: {ex}");
                return false;
            }
        }

        /// <summary>Тема уже применена хелпером (для диагностики и тестов).</summary>
        public static bool IsApplied(Window window)
        {
            if (window == null)
            {
                return false;
            }

            lock (Applied)
            {
                return Applied.TryGetValue(window, out _);
            }
        }

        /// <summary>
        /// Является ли окно диалогом WF (редактор выражений, редактор аргумента и т.п.).
        /// Проверка по типам, а не по ссылке на сборку WF: типы <c>EditorDialog</c> внутренние.
        /// </summary>
        public static bool IsWfDialog(Window window)
        {
            if (window == null)
            {
                return false;
            }

            Type type = window.GetType();
            if (!string.Equals(type.Namespace, "System.Activities.Presentation", StringComparison.Ordinal)
                && !string.Equals(type.Namespace, "System.Activities.Presentation.View", StringComparison.Ordinal))
            {
                // Окна студии (RPA.*) — не наш случай: они уже на теме студии.
                return false;
            }

            return string.Equals(type.Name, "WorkflowElementDialogWindow", StringComparison.Ordinal)
                   || string.Equals(type.Name, "EditorDialog", StringComparison.Ordinal)
                   || string.Equals(type.BaseType?.Name, "WorkflowElementDialog", StringComparison.Ordinal);
        }

        private static void ApplyWindowChrome(Window window)
        {
            if (WindowChrome.GetWindowChrome(window) is WindowChrome existing)
            {
                // У окна уже есть chrome (диалог WF мог создать свой) — правим только caption.
                existing.CaptionHeight = 0;
                existing.GlassFrameThickness = new Thickness(0);
                return;
            }

            WindowChrome.SetWindowChrome(window, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(ResizeBorderThickness),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(StudioPalette.WindowCornerRadius),
                UseAeroCaptionButtons = false
            });
        }

        private static void ApplyPalette(Window window)
        {
            // DynamicResource вместо прямых кистей: смена темы на лету не оставит окно светлым.
            window.SetResourceReference(Control.BackgroundProperty, StudioBrushes.SurfaceKey);
            window.SetResourceReference(Control.ForegroundProperty, StudioBrushes.ForegroundKey);
            window.SetResourceReference(Control.FontFamilyProperty, StudioFonts.FontFamilyKey);
        }

        private static void WrapContent(Window window)
        {
            if (window.Content is StudioTitleBar)
            {
                // Окно уже обёрнуто (например, Apply позвали дважды с разных точек).
                return;
            }

            object content = window.Content;

            // Отцепляем прежнее содержимое: элемент не может быть ребёнком двух родителей,
            // а WF-диалог держит своё содержимое именно в Content окна.
            window.Content = null;

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleBarHeight) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            StudioTitleBar titleBar = new StudioTitleBar();
            Grid.SetRow(titleBar, 0);
            _ = root.Children.Add(titleBar);

            if (content is UIElement element)
            {
                Grid.SetRow(element, 1);
                _ = root.Children.Add(element);
            }
            else if (content != null)
            {
                ContentControl host = new ContentControl { Content = content };
                Grid.SetRow(host, 1);
                _ = root.Children.Add(host);
            }

            window.Content = root;
        }
    }
}
