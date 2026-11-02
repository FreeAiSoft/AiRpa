namespace RPA.Core.Services
{
    /// <summary>
    /// Автомат флагов контекстного меню переменных/аргументов (документ —
    /// <c>docs\Designer_Variable_ContextMenu_Principles.md</c>).
    /// <para>
    /// Было два независимых <c>bool</c>-поля, и флаг «меню открывается» сбрасывался только внутри
    /// <c>CellEditEnding</c>. Если правый клик не приводил к завершению правки (клик по заголовку,
    /// закрытие меню мышью, повторный клик), флаг оставался взведённым и следующее завершение правки
    /// отменялось без причины — ячейка «залипала» в режиме редактирования.
    /// </para>
    /// <para>
    /// Здесь отмена работает по принципу consume-once: <see cref="NotifyCellEditEnding"/> возвращает
    /// решение и <b>всегда</b> сбрасывает взвод, поэтому одного взвода хватает максимум на одно
    /// завершение правки. Открытие меню взвод не снимает — меню открывается отложенно
    /// (<c>DispatcherPriority.Input</c>), а <c>CellEditEnding</c> приходит после <c>Opened</c>.
    /// </para>
    /// </summary>
    internal sealed class DesignerContextMenuState
    {
        private bool _isCancelArmed;

        /// <summary>Меню открыто (нужно тестам и для диагностики).</summary>
        public bool IsMenuOpen { get; private set; }

        /// <summary>Отмена завершения правки взведена правым кликом в поле ввода.</summary>
        public bool IsCancelArmed => _isCancelArmed;

        /// <summary>Правый клик в поле ввода ячейки: следующее завершение правки отменяется.</summary>
        public void NotifyRightButtonDown()
        {
            _isCancelArmed = true;
        }

        /// <summary>Меню открылось: взвод отмены сохраняется до <see cref="NotifyCellEditEnding"/>.</summary>
        public void NotifyMenuOpened()
        {
            IsMenuOpen = true;
        }

        /// <summary>Меню закрылось любым путём (пункт меню, клик вне, Escape): сброс автомата.</summary>
        public void NotifyMenuClosed()
        {
            IsMenuOpen = false;
            _isCancelArmed = false;
        }

        /// <summary>
        /// Завершение правки ячейки: вернуть <c>true</c>, если правку нужно отменить.
        /// Взвод снимается всегда — второй <c>CellEditEnding</c> уже не отменяется.
        /// </summary>
        public bool NotifyCellEditEnding()
        {
            bool cancel = _isCancelArmed;
            _isCancelArmed = false;
            return cancel;
        }
    }
}
