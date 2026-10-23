using System;

namespace LogControlLibrary.Models
{
    /// <summary>
    /// Перечисление уровней логирования
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// Подробная трассировка выполнения
        /// </summary>
        Trace = 0,

        /// <summary>
        /// Отладочная информация
        /// </summary>
        Debug = 1,

        /// <summary>
        /// Информационные сообщения
        /// </summary>
        Info = 2,

        /// <summary>
        /// Предупреждения
        /// </summary>
        Warning = 3,

        /// <summary>
        /// Ошибки
        /// </summary>
        Error = 4,

        /// <summary>
        /// Критические ошибки
        /// </summary>
        Fatal = 5
    }
}