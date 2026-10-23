using System;
using System.ComponentModel;

namespace RPA.AiAgent.Models
{
    /// <summary>
    /// Тип сообщения в чате
    /// </summary>
    public enum MessageType
    {
        /// <summary>
        /// Обычное сообщение пользователя или ассистента
        /// </summary>
        Normal,

        /// <summary>
        /// Системное сообщение
        /// </summary>
        System,

        /// <summary>
        /// MCP команда
        /// </summary>
        McpCommand
    }

    /// <summary>
    /// Представляет одно сообщение в чате
    /// </summary>
    public class ChatMessage : INotifyPropertyChanged
    {
        private string _content;
        private bool _isStreaming;
        private MessageType _messageType;

        /// <summary>
        /// Роль отправителя: "user" или "assistant"
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// Тип сообщения
        /// </summary>
        public MessageType Type
        {
            get => _messageType;
            set
            {
                if (_messageType != value)
                {
                    _messageType = value;
                    OnPropertyChanged(nameof(Type));
                }
            }
        }

        /// <summary>
        /// Содержимое сообщения
        /// </summary>
        public string Content
        {
            get => _content;
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
            get => _isStreaming;
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
            Type = MessageType.Normal;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
