# AiAgentChatControl - Контрол чата с Ollama

## Описание проекта

WPF UserControl для интеграции чата с локальным AI через Ollama. Поддерживает потоковую передачу ответов, выбор моделей и сохранение истории диалогов.

## Основные возможности

✅ **Работа с Ollama API**
- Получение списка доступных локальных моделей
- Выбор модели из выпадающего списка
- Проверка доступности Ollama

✅ **Чат функциональность**
- Отправка сообщений пользователя
- Потоковое получение ответов от AI (streaming)
- Отображение истории диалога
- Визуальное разделение сообщений пользователя и AI

✅ **Управление диалогами**
- Сохранение истории диалогов в JSON файлы
- Загрузка предыдущих диалогов
- Очистка текущего чата
- Автоматическое обновление при изменениях

✅ **UI/UX**
- MVVM архитектура
- Responsive дизайн
- Разные цвета для сообщений пользователя и AI
- Индикатор потоковой передачи

## Технологический стек

- **Platform**: WPF (.NET Framework 4.7.2)
- **Pattern**: MVVM
- **Serialization**: Newtonsoft.Json
- **HTTP Client**: System.Net.Http
- **AI Backend**: Ollama (localhost:11434)

## Структура проекта

```
AiAgent/
├── Control/                          # UI контролы
│   ├── AiAgentChatControl.xaml
│   └── AiAgentChatControl.xaml.cs
├── Models/                           # Модели данных
│   ├── ChatMessage.cs
│   ├── Conversation.cs
│   ├── OllamaModel.cs
│   └── OllamaResponse.cs
├── Services/                         # Бизнес-логика
│   ├── IOllamaService.cs
│   ├── OllamaService.cs
│   ├── IConversationStorage.cs
│   └── JsonConversationStorage.cs
├── ViewModels/                       # MVVM ViewModels
│   ├── AiAgentChatViewModel.cs
│   └── RelayCommand.cs
├── Converters/                       # WPF конверторы
│   ├── MessageAlignmentConverter.cs
│   ├── MessageBackgroundConverter.cs
│   └── BoolToVisibilityConverter.cs
└── docs/                             # Документация
    ├── Architecture.md               # Детальная архитектура
    ├── ImplementationPlan.md         # План реализации
    └── README.md                     # Этот файл
```

## Требования

### Системные требования
- Windows 10 или выше
- .NET Framework 4.7.2
- Visual Studio 2019 или выше

### Зависимости
- Ollama Desktop Application (установлен и запущен)
- Минимум одна загруженная модель в Ollama
- NuGet пакет: Newtonsoft.Json 13.0.3+

## Установка Ollama

1. Скачать Ollama с [официального сайта](https://ollama.ai)
2. Установить приложение
3. Загрузить модель (например):
   ```bash
   ollama pull llama2
   ```
4. Проверить, что Ollama запущен (должен отвечать на http://localhost:11434)

## Быстрый старт

### 1. Установка NuGet пакета
```xml
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
```

### 2. Использование контрола в XAML
```xml
<Window xmlns:local="clr-namespace:AiAgent.Control;assembly=AiAgent">
    <Grid>
        <local:AiAgentChatControl />
    </Grid>
</Window>
```

### 3. Программное использование
```csharp
var chatControl = new AiAgentChatControl();
this.Content = chatControl;
```

## Документация

### Подробная документация
- [**Architecture.md**](Architecture.md) - Полная архитектура проекта
- [**ImplementationPlan.md**](ImplementationPlan.md) - Пошаговый план реализации

### Ключевые классы

**OllamaService** - взаимодействие с Ollama API
```csharp
public interface IOllamaService
{
    Task<List<OllamaModel>> GetModelsAsync();
    IAsyncEnumerable<string> ChatStreamAsync(string model, List<ChatMessage> messages);
    Task<bool> IsOllamaRunningAsync();
}
```

**ConversationStorage** - сохранение диалогов
```csharp
public interface IConversationStorage
{
    Task SaveConversationAsync(Conversation conversation);
    Task<Conversation> LoadConversationAsync(Guid id);
    Task<List<Conversation>> GetAllConversationsAsync();
}
```

**AiAgentChatViewModel** - основная ViewModel
```csharp
public class AiAgentChatViewModel : INotifyPropertyChanged
{
    public ObservableCollection<ChatMessage> Messages { get; }
    public ObservableCollection<OllamaModel> AvailableModels { get; }
    public ICommand SendMessageCommand { get; }
    public ICommand LoadModelsCommand { get; }
}
```

## Примеры использования

### Пример 1: Базовое использование
```csharp
// В конструкторе окна
var viewModel = new AiAgentChatViewModel(
    new OllamaService(),
    new JsonConversationStorage()
);

chatControl.DataContext = viewModel;
```

### Пример 2: Обработка событий
```csharp
viewModel.PropertyChanged += (s, e) =>
{
    if (e.PropertyName == nameof(viewModel.IsSending))
    {
        // Реагировать на отправку сообщения
    }
};
```

## Архитектурные решения

### MVVM паттерн
- **Model**: [`ChatMessage`](../Models/ChatMessage.cs), [`Conversation`](../Models/Conversation.cs)
- **View**: [`AiAgentChatControl.xaml`](../Control/AiAgentChatControl.xaml)
- **ViewModel**: [`AiAgentChatViewModel`](../ViewModels/AiAgentChatViewModel.cs)

### Dependency Injection
```csharp
public AiAgentChatViewModel(
    IOllamaService ollamaService,
    IConversationStorage conversationStorage)
{
    _ollamaService = ollamaService;
    _conversationStorage = conversationStorage;
}
```

### Асинхронность
- Все операции с API асинхронные
- Потоковая передача через `IAsyncEnumerable<string>`
- CancellationToken для отмены запросов

## Производительность

### Оптимизации
- Виртуализация списка сообщений
- Debounce для UI обновлений при streaming
- Lazy loading старых диалогов
- Ограничение количества сообщений в памяти

### Управление ресурсами
- Правильный Dispose для HTTP клиента
- Отмена активных запросов при закрытии
- Очистка event handlers

## Расширяемость

### Возможные улучшения
1. **Markdown рендеринг** - форматированный текст
2. **Подсветка кода** - syntax highlighting
3. **Экспорт диалогов** - PDF, Markdown, HTML
4. **Настройки модели** - temperature, top_p, max_tokens
5. **Мультимодальность** - поддержка изображений
6. **Темы оформления** - светлая/темная
7. **Голосовой ввод** - speech-to-text
8. **Множественные диалоги** - вкладки или боковая панель

## Тестирование

### Unit тесты
```csharp
[Test]
public async Task SendMessage_Should_AddToMessages()
{
    var viewModel = new AiAgentChatViewModel(mockService, mockStorage);
    await viewModel.SendMessageCommand.Execute(null);
    Assert.IsTrue(viewModel.Messages.Count > 0);
}
```

### Integration тесты
- Полный цикл: отправка → получение → сохранение
- Streaming функциональность
- Загрузка моделей

## Устранение неполадок

### Ollama не отвечает
```
Ошибка: Не удалось подключиться к Ollama
Решение: Проверьте, что Ollama запущен (http://localhost:11434)
```

### Нет доступных моделей
```
Ошибка: Список моделей пуст
Решение: Загрузите модель через команду: ollama pull llama2
```

### Ошибка сериализации
```
Ошибка: JsonSerializationException
Решение: Убедитесь, что установлен Newtonsoft.Json
```

## Лицензия

MIT License

## Авторы

Проект разработан для интеграции с Ollama AI

## Контакты

Для вопросов и предложений создавайте issues в репозитории проекта.

---

**Статус проекта**: 🎨 Архитектура завершена, готов к реализации

**Следующий шаг**: Переход в режим Code для имплементации