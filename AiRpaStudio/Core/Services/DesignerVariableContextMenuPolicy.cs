using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using RPA.Helpers;

namespace RPA.Core.Services
{
    /// <summary>
    /// Правила контекстного меню в таблицах переменных и аргументов дизайнера, вынесенные в чистые
    /// функции (документ — <c>docs\Designer_Variable_ContextMenu_Principles.md</c>).
    /// <para>
    /// Зачем отдельный тип: штатное <see cref="ContextMenu"/> на ячейке <see cref="DataGrid"/> в
    /// WF-дизайнере не работает — правый клик завершает правку ячейки и закрывает панель переменных,
    /// поэтому меню открывается программно, а правила «куда пришёл клик», «когда взводить отмену
    /// завершения правки» и «когда закрывать меню» должны быть проверяемы без HWND, без
    /// <c>WorkflowDesigner</c> и без реального ввода мышью.
    /// </para>
    /// <para>
    /// Инвариант порядка: все обработчики живут на <see cref="DataGrid"/>. Его
    /// <see cref="UIElement.PreviewMouseRightButtonDownEvent"/> туннелирует сверху вниз, поэтому таблица
    /// успевает пометить событие обработанным и взвести отмену раньше ячейки. Если перенести обработчик
    /// вглубь (в стиль поля ввода, в шаблон ячейки), <c>CellEditEnding</c> перестаёт отменяться,
    /// правка теряется, а запись в <c>DataTemplate.Resources</c> роняет панель (дефект 1.0.0.0).
    /// </para>
    /// </summary>
    internal static class DesignerVariableContextMenuPolicy
    {
        /// <summary>Можно ли подключать обработчики: нужна таблица хотя бы с одним столбцом.</summary>
        public static bool CanAttach(DataGrid dataGrid)
        {
            return dataGrid != null && dataGrid.Columns != null && dataGrid.Columns.Count > 0;
        }

        /// <summary>
        /// Поле ввода, в котором произошёл клик: сам <see cref="TextBox"/> или его визуальный потомок
        /// (текст, каретка). <c>null</c> — клик мимо поля правки (заголовок, скроллбар, пустая область).
        /// <para>
        /// Это же правило взводит отмену завершения правки и выбирает поле для меню: раньше проверка
        /// дублировалась, и «клик мимо» взводил флаг, который глушил следующую правку.
        /// </para>
        /// </summary>
        public static TextBox FindEditingField(DependencyObject originalSource)
        {
            if (originalSource == null)
            {
                return null;
            }

            if (originalSource is TextBox textBox)
            {
                return textBox;
            }

            return XamlHelper.FindVisualParent<TextBox>(originalSource);
        }

        /// <summary>
        /// Нужно ли инициализировать таблицу при изменении видимости панели: панели переменных и
        /// аргументов строятся лениво, поэтому подключение идёт по <see cref="UIElement.IsVisibleChanged"/>
        /// и ровно один раз на панель.
        /// </summary>
        public static bool ShouldInitializeOnVisibilityChanged(bool isVisible, bool alreadyInitialized)
        {
            return isVisible && !alreadyInitialized;
        }

        /// <summary>
        /// Взводить ли отмену завершения правки: только если правый клик пришёл в поле ввода ячейки.
        /// Иначе клик мимо поля (заголовок, скроллбар, пустая область) оставлял взведённый флаг и
        /// глушил следующее, уже пользовательское, завершение правки.
        /// </summary>
        public static bool ShouldArmCancel(DependencyObject originalSource)
        {
            return FindEditingField(originalSource) != null;
        }

        /// <summary>Закрывать ли меню по нажатию кнопки мыши: клик вне меню и вне поля ввода.</summary>
        public static bool ShouldCloseOnMouseDown(bool isPointerOverMenu, bool isPointerOverField)
        {
            return !isPointerOverMenu && !isPointerOverField;
        }

        /// <summary>Закрывать ли меню по клавише: только Escape (иначе ломаются Ctrl+C/Ctrl+V).</summary>
        public static bool ShouldCloseOnKey(Key key)
        {
            return key == Key.Escape;
        }

        /// <summary>
        /// Позиционирование меню: у точки мыши, владелец — поле ввода ячейки.
        /// Меню открывается программно, поэтому <c>PlacementTarget</c> обязан быть задан явно —
        /// иначе <see cref="ContextMenu"/> не знает, где рисоваться, и открывается в углу экрана.
        /// </summary>
        public static void ConfigureMenuPlacement(ContextMenu menu, UIElement target)
        {
            if (menu == null || target == null)
            {
                return;
            }

            menu.PlacementTarget = target;
            menu.Placement = PlacementMode.MousePoint;
        }
    }
}
