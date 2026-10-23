# План реализации AiAgentChatControl

## Порядок реализации

### Этап 1: Базовые модели данных
1. Создать `Models/ChatMessage.cs` - модель сообщения чата
2. Создать `Models/Conversation.cs` - модель диалога
3. Создать `Models/OllamaModel.cs` - модель для информации о модели Ollama
4. Создать `Models/OllamaResponse.cs` - модель ответа от Ollama API

### Этап 2: Сервисный слой
5. Создать `Services/IOllamaService.cs` - интерфейс для работы с Ollama
6. Создать `Services/OllamaService.cs` - реализация клиента Ollama API
7. Создать `Services/IConversationStorage.cs` - интерфейс хранилища
8. Создать `Services/JsonConversationStorage.cs` - сохранение в JSON

### Этап 3: ViewModel и команды
9. Создать `ViewModels/RelayCommand.cs` - helper для команд
10. Создать `ViewModels/AiAgentChatViewModel.cs` - главная ViewModel

### Этап 4: UI конверторы
11. Создать `Converters/MessageAlignmentConverter.cs` - выравнивание сообщений
12. Создать `Converters/MessageBackgroundConverter.cs` - цвет фона сообщений
13. Создать `Converters/BoolToVisibilityConverter.cs` - видимость элементов

### Этап 5: XAML и UI
14. Обновить `Control/AiAgentChatControl.xaml` - создать UI интерфейс
15. Обновить `Control/AiAgentChatControl.xaml.cs` - инициализация ViewModel

### Этап 6: Конфигурация проекта
16. Обновить `AiAgent.csproj` - добавить NuGet пакеты и новые файлы

### Этап 7: Тестирование
17. Создать простое тестовое приложение для проверки контрола

## Детали реализации

### 1. ChatMessage.cs
```csharp
using System;
using System.ComponentModel;

namespace AiAgent.Models
{
    public class ChatMessage : INotifyPropertyChanged
    {
        private string _content;
        private bool _isStreaming;

        public string Role { get; set; }
        
        public string Content
        {
            get => _content;
            set
            {
                _content = value;
                OnPropertyChanged(nameof(Content));
            }
        }
        
        public DateTime Timestamp { get; set; }
        
        public bool IsStreaming
        {
            get => _isStreaming;
            set
            {
                _isStreaming = value;
                OnPropertyChanged(nameof(IsStreaming));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
```

### 2. Conversation.cs
```csharp
using System;
using System.Collections.ObjectModel;

namespace AiAgent.Models
{
    public class Conversation
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string ModelName { get; set; }
        public ObservableCollection<ChatMessage> Messages { get; set; }

        public Conversation()
        {
            Id = Guid.NewGuid();
            CreatedAt = DateTime.Now;
            UpdatedAt = DateTime.Now;
            Messages = new ObservableCollection<ChatMessage>();
        }
    }
}
```

### 3. OllamaModel.cs
```csharp
using System;

namespace AiAgent.Models
{
    public class OllamaModel
    {
        public string Name { get; set; }
        public DateTime ModifiedAt { get; set; }
        public long Size { get; set; }
        
        public string DisplayName => Name;
        public string SizeFormatted => FormatBytes(Size);
        
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
```

### 4. OllamaResponse.cs
```csharp
using Newtonsoft.Json;

namespace AiAgent.Models
{
    public class OllamaResponse
    {
        [JsonProperty("model")]
        public string Model { get; set; }
        
        [JsonProperty("message")]
        public OllamaMessage Message { get; set; }
        
        [JsonProperty("done")]
        public bool Done { get; set; }
    }
    
    public class OllamaMessage
    {
        [JsonProperty("role")]
        public string Role { get; set; }
        
        [JsonProperty("content")]
        public string Content { get; set; }
    }
    
    public class OllamaModelsResponse
    {
        [JsonProperty("models")]
        public OllamaModelInfo[] Models { get; set; }
    }
    
    public class OllamaModelInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        
        [JsonProperty("modified_at")]
        public string ModifiedAt { get; set; }
        
        [JsonProperty("size")]
        public long Size { get; set; }
    }
}
```

### 5-8. Сервисы (интерфейсы и реализации)
Реализовать согласно архитектуре в Architecture.md

### 9. RelayCommand.cs
```csharp
using System;
using System.Windows.Input;

namespace AiAgent.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }
    }
}
```

### 10. AiAgentChatViewModel.cs
Основная ViewModel с:
- ObservableCollection для сообщений и моделей
- Команды для отправки, очистки, загрузки моделей
- Логика потоковой обработки ответов
- Сохранение/загрузка диалогов

### 11-13. Конверторы
Конверторы для WPF binding:
- Выравнивание сообщений (user - справа, assistant - слева)
- Цвет фона (разные цвета для user/assistant)
- Видимость элементов

### 14. AiAgentChatControl.xaml
Полный UI с:
- ComboBox для выбора модели
- ItemsControl для отображения сообщений
- TextBox для ввода
- Кнопки управления

### 15. AiAgentChatControl.xaml.cs
Инициализация ViewModel и привязка DataContext

### 16. AiAgent.csproj
Добавить:
- NuGet пакет Newtonsoft.Json
- Все новые файлы в проект
- Необходимые references

## Порядок разработки

1. **Сначала** - модели данных (простые POCO классы)
2. **Затем** - сервисы (логика без UI)
3. **Потом** - ViewModel (связующее звено)
4. **После** - конверторы (вспомогательные классы)
5. **В конце** - XAML (визуальная часть)

## Проверка работоспособности

После реализации необходимо:
1. Запустить Ollama локально
2. Загрузить хотя бы одну модель (например: `ollama pull llama2`)
3. Создать тестовое WPF приложение с контролом
4. Проверить:
   - Загрузку списка моделей
   - Отправку сообщения
   - Получение потокового ответа
   - Сохранение диалога
   - Очистку чата

## Зависимости

### NuGet пакеты
- **Newtonsoft.Json** (версия 13.0.3 или выше)

### Системные требования
- .NET Framework 4.7.2
- Ollama установлен и запущен на `http://localhost:11434`
- Хотя бы одна загруженная модель в Ollama