using System;

namespace LogControlLibrary.Models
{
    /// <summary>
    /// Интерфейс для записи лога, обеспечивает расширяемость системы логирования
    /// </summary>
    public interface ILogEntry
    {
        /// <summary>
        /// Уровень логирования
        /// </summary>
        LogLevel Level { get; set; }

        /// <summary>
        /// Время создания записи лога
        /// </summary>
        DateTime Timestamp { get; set; }

        /// <summary>
        /// Текст сообщения лога
        /// </summary>
        string Message { get; set; }

        /// <summary>
        /// Дополнительные данные лога (опционально)
        /// </summary>
        object Tag { get; set; }

        /// <summary>
        /// Уникальный идентификатор записи лога
        /// </summary>
        long Id { get; set; }
    }
}