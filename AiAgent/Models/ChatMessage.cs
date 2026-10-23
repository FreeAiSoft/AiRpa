using System;
using System.ComponentModel;

namespace AiAgent.Models
{
    /// <summary>
    /// Представляет одно сообщение в чате
    /// </summary>
    public class ChatMessage : INotifyPropertyChanged
    {
        private string _content;
        private bool _isStreaming;

        /// <summary>
        /// Роль отправителя: "user" или "assistant"
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// Содержимое сообщения
        /// </summary>
        public string Content
        {
            get { return _content; }
            set
            {
                if (_content != value)
                {
                    _content = value;
                    OnPropertyChanged(nameof(Content));
                }
            }
        }

        /// <summary>
        /// Время отправки сообщения
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Флаг, указывающий на потоковую передачу
        /// </summary>
        public bool IsStreaming
        {
            get { return _isStreaming; }
            set
            {
                if (_isStreaming != value)
                {
                    _isStreaming = value;
                    OnPropertyChanged(nameof(IsStreaming));
                }
            }
        }

        public ChatMessage()
        {
            Timestamp = DateTime.Now;
            Content = string.Empty;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}