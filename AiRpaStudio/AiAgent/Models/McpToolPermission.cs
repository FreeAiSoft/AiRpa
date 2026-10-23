using System.ComponentModel;

namespace RPA.AiAgent.Models
{
    /// <summary>
    /// Модель разрешения для MCP инструмента
    /// </summary>
    public class McpToolPermission : INotifyPropertyChanged
    {
        private bool _isApproved;

        /// <summary>
        /// Имя инструмента (техническое)
        /// </summary>
        public string ToolName { get; set; }

        /// <summary>
        /// Отображаемое имя
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Категория инструмента (Read, Write, Execute, MCP, Navigate)
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Иконка Material Design
        /// </summary>
        public string Icon { get; set; }

        /// <summary>
        /// Одобрен ли инструмент для автоматического выполнения
        /// </summary>
        public bool IsApproved
        {
            get => _isApproved;
            set
            {
                if (_isApproved != value)
                {
                    _isApproved = value;
                    OnPropertyChanged(nameof(IsApproved));
                }
            }
        }

        /// <summary>
        /// Описание инструмента
        /// </summary>
        public string Description { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Настройки разрешений MCP инструментов для сериализации
    /// </summary>
    public class McpPermissionsSettings
    {
        public bool GlobalEnabled { get; set; }
        public System.Collections.Generic.List<ToolPermissionData> Permissions { get; set; }

        public McpPermissionsSettings()
        {
            Permissions = new System.Collections.Generic.List<ToolPermissionData>();
            GlobalEnabled = false;
        }
    }

    /// <summary>
    /// Данные разрешения для сериализации
    /// </summary>
    public class ToolPermissionData
    {
        public string ToolName { get; set; }
        public bool IsApproved { get; set; }
    }
}