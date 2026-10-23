using System;
using System.ComponentModel;

namespace LogControlLibrary.Models
{
    /// <summary>
    /// Основная реализация записи лога
    /// </summary>
    public class LogEntry : ILogEntry, INotifyPropertyChanged
    {
        private static long _idCounter = 0;
        private LogLevel _level;
        private DateTime _timestamp;
        private string _message;
        private object _tag;
        private long _id;

        /// <summary>
        /// Конструктор по умолчанию
        /// </summary>
        public LogEntry()
        {
            _id = System.Threading.Interlocked.Increment(ref _idCounter);
            _timestamp = DateTime.Now;
            _message = string.Empty;
            _level = LogLevel.Info;
        }

        /// <summary>
        /// Конструктор с параметрами
        /// </summary>
        /// <param name="level">Уровень логирования</param>
        /// <param name="message">Сообщение</param>
        /// <param name="timestamp">Время (опционально, по умолчанию текущее время)</param>
        public LogEntry(LogLevel level, string message, DateTime? timestamp = null)
        {
            _id = System.Threading.Interlocked.Increment(ref _idCounter);
            _level = level;
            _message = message ?? string.Empty;
            _timestamp = timestamp ?? DateTime.Now;
        }

        /// <summary>
        /// Уникальный идентификатор записи лога
        /// </summary>
        public long Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged(nameof(Id));
                }
            }
        }

        /// <summary>
        /// Уровень логирования
        /// </summary>
        public LogLevel Level
        {
            get => _level;
            set
            {
                if (_level != value)
                {
                    _level = value;
                    OnPropertyChanged(nameof(Level));
                }
            }
        }

        /// <summary>
        /// Время создания записи лога
        /// </summary>
        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                if (_timestamp != value)
                {
                    _timestamp = value;
                    OnPropertyChanged(nameof(Timestamp));
                }
            }
        }

        /// <summary>
        /// Текст сообщения лога
        /// </summary>
        public string Message
        {
            get => _message;
            set
            {
                if (_message != value)
                {
                    _message = value ?? string.Empty;
                    OnPropertyChanged(nameof(Message));
                }
            }
        }

        /// <summary>
        /// Дополнительные данные лога
        /// </summary>
        public object Tag
        {
            get => _tag;
            set
            {
                if (_tag != value)
                {
                    _tag = value;
                    OnPropertyChanged(nameof(Tag));
                }
            }
        }

        /// <summary>
        /// Событие изменения свойства для биндинга
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Уведомление об изменении свойства
        /// </summary>
        /// <param name="propertyName">Имя изменившегося свойства</param>
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Строковое представление записи лога
        /// </summary>
        /// <returns>Отформатированная строка лога</returns>
        public override string ToString()
        {
            return $"[{Timestamp:HH:mm:ss.fff}] [{Level}] {Message}";
        }

        /// <summary>
        /// Проверка равенства записей лога
        /// </summary>
        /// <param name="obj">Объект для сравнения</param>
        /// <returns>True, если записи равны</returns>
        public override bool Equals(object obj)
        {
            if (obj is LogEntry other)
            {
                return Id == other.Id;
            }
            return false;
        }

        /// <summary>
        /// Получение хэш-кода записи
        /// </summary>
        /// <returns>Хэш-код</returns>
        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }
    }
}