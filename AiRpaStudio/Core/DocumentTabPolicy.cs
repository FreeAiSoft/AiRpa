using System;
using System.Collections.Generic;

namespace RPA.Core
{
    /// <summary>
    /// Правило «какая вкладка становится активной после закрытия» — единственное место правды для
    /// панели документов студии (баг «при закрытии открывается не та вкладка»).
    /// <para>
    /// Поведение как в Visual Studio: активной становится соседняя <b>справа</b> вкладка (после
    /// удаления закрытой она сдвигается на её место), для последней вкладки — соседняя <b>слева</b>.
    /// Закрытие единственной вкладки оставляет панель без активной вкладки, закрытие невыбранной
    /// (фоновой) вкладки активную не меняет вовсе — панель документов в этом случае соседа не
    /// назначает. Раньше правило не применялось вообще: студия выбирала нулевую вкладку
    /// (<c>WorkflowDesigners[0]</c>), то есть всегда первую, и пользователь «прыгал» не туда.
    /// </para>
    /// <para>
    /// Здесь нет ни WPF, ни AvalonDock: правило проверяется тестами без окна и без диспетчера, а
    /// панель документов (<see cref="DocumentTabsCloseCoordinator"/>) обязана спрашивать его, а не
    /// выбирать вкладку самостоятельно. Список вкладок передаётся в порядке <b>до</b> закрытия, а
    /// вкладки сравниваются <b>по ссылке</b> (путь к файлу может совпасть у двух дизайнеров).
    /// </para>
    /// </summary>
    public static class DocumentTabPolicy
    {
        /// <summary>
        /// Индекс соседней вкладки (в списке <b>до</b> закрытия), которая становится активной, или
        /// <c>null</c>, если активировать нечего (вкладка была единственной).
        /// </summary>
        /// <param name="closedIndex">Индекс закрываемой вкладки в порядке <b>до</b> закрытия.</param>
        /// <param name="countBeforeClose">Количество вкладок <b>до</b> закрытия.</param>
        public static int? NextActiveIndex(int closedIndex, int countBeforeClose)
        {
            if (countBeforeClose <= 1)
            {
                return null;
            }

            if (closedIndex < 0 || closedIndex >= countBeforeClose)
            {
                // Индекс вне списка: закрывали не вкладку этой панели — выбирать нечего.
                return null;
            }

            // Индексы считаются по списку ДО закрытия: правый сосед — closedIndex + 1, левый — closedIndex - 1.
            return closedIndex < countBeforeClose - 1 ? closedIndex + 1 : closedIndex - 1;
        }

        /// <summary>
        /// Соседняя вкладка для закрываемой <paramref name="closing"/> из списка вкладок
        /// <b>до</b> закрытия (порядок — как в панели документов). <c>null</c> — активировать нечего.
        /// </summary>
        public static T NextActive<T>(IReadOnlyList<T> tabsBeforeClose, T closing) where T : class
        {
            if (tabsBeforeClose == null || tabsBeforeClose.Count == 0 || closing == null)
            {
                return null;
            }

            int? nextIndex = NextActiveIndex(IndexOf(tabsBeforeClose, closing), tabsBeforeClose.Count);
            return nextIndex.HasValue ? tabsBeforeClose[nextIndex.Value] : null;
        }

        private static int IndexOf<T>(IReadOnlyList<T> tabs, T item) where T : class
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                // Вкладки сравниваются по ссылке: путь к файлу может совпасть у разных документов
                // (два дизайнера на один файл), а закрываем мы конкретный экземпляр.
                if (ReferenceEquals(tabs[i], item))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
