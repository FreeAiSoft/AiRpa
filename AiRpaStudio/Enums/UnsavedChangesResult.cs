namespace RPA.Enums
{
    /// <summary>
    /// Результат темизированного диалога «Есть несохранённые изменения».
    /// </summary>
    public enum UnsavedChangesResult
    {
        /// <summary>Сохранить все несохранённые изменения и продолжить.</summary>
        Save,

        /// <summary>Продолжить без сохранения.</summary>
        Discard,

        /// <summary>Отменить действие (не закрывать студию / не переключать проект).</summary>
        Cancel
    }
}
