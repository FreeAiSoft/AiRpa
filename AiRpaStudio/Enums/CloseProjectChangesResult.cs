namespace RPA.Enums
{
    /// <summary>
    /// Результат темизированного диалога «Закрытие проекта с несохранёнными изменениями».
    /// </summary>
    public enum CloseProjectChangesResult
    {
        /// <summary>Сохранить изменения на диск и закрыть проект.</summary>
        Save,

        /// <summary>Сохранить изменения в буфер проекта (для последующего открытия) и закрыть проект.</summary>
        SaveToBuffer,

        /// <summary>Отменить закрытие проекта.</summary>
        Cancel
    }
}
