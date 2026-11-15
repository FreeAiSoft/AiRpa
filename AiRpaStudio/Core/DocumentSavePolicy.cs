using System;

namespace RPA.Core
{
    /// <summary>
    /// Единое правило кнопок «Сохранить»/«Сохранить всё» для панели документов студии.
    /// <para>
    /// Здесь нет ни WPF, ни WF: правило чистое и проверяется юнит-тестами. Оно учитывает оба вида
    /// вкладок — дизайнеры (<c>WorkflowDesignerEx.IsEdited</c>) и редакторы кода/XAML
    /// (<c>CodeDocument.IsDirty</c>), потому что кнопка «Сохранить» должна оживать и для активной
    /// вкладки кода, а не только для активного дизайнера.
    /// </para>
    /// </summary>
    public static class DocumentSavePolicy
    {
        /// <summary>
        /// «Сохранить» активно только на остановленном workflow, когда есть активная вкладка и она
        /// изменена (дизайнер — по <c>IsEdited</c>, редактор кода — по <c>IsDirty</c>).
        /// </summary>
        public static bool CanSave(bool stopped, bool hasActiveDocument, bool activeDesignerEdited, bool activeCodeDirty)
        {
            return stopped
                && hasActiveDocument
                && (activeDesignerEdited || activeCodeDirty);
        }

        /// <summary>
        /// «Сохранить всё» активно только на остановленном workflow, когда изменён хотя бы один
        /// дизайнер или хотя бы один редактор кода/XAML.
        /// </summary>
        public static bool CanSaveAll(bool stopped, bool anyDesignerEdited, bool anyCodeDirty)
        {
            return stopped && (anyDesignerEdited || anyCodeDirty);
        }
    }
}
