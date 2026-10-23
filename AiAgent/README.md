# AiAgentChatControl

WPF контрол для чата с локальным AI через Ollama.

## Возможности

- ✅ Получение списка доступных моделей Ollama
- ✅ Выбор модели из выпадающего списка
- ✅ Отправка сообщений и получение ответов
- ✅ Потоковая передача ответов (streaming)
- ✅ Сохранение истории диалогов в JSON
- ✅ Визуальное разделение сообщений пользователя и AI
- ✅ Возможность отмены генерации ответа

## Требования

1. **Ollama Desktop Application** - должен быть установлен и запущен
2. **.NET Framework 4.7.2** или выше
3. **Visual Studio 2019** или выше

## Установка Ollama

1. Скачайте Ollama с [официального сайта](https://ollama.ai)
2. Установите приложение
3. Загрузите модель (например):
   ```bash
   ollama pull llama2
   ```
4. Убедитесь, что Ollama запущен (должен отвечать на http://localhost:11434)

## Сборка проекта

1. Откройте проект в Visual Studio
2. Восстановите NuGet пакеты:
   ```
   Tools → NuGet Package Manager → Restore NuGet Packages
   ```
   Или выполните в Package Manager Console:
   ```
   Install-Package Newtonsoft.Json -Version 13.0.3
   ```
3. Соберите проект (Build → Build Solution)

## Использование

### В XAML

```xml
<Window xmlns:local="clr-namespace:AiAgent.Control;assembly=AiAgent">
    <Grid>
        <local:AiAgentChatControl />
    </Grid>
</Window>
```

### Программно

```csharp
var chatControl = new AiAgentChatControl();
this.Content = chatControl;
```

## Структура проекта

```
AiAgent/
├── Control/                 # UI контролы
├── Models/                  # Модели данных
├── Services/                # Бизнес-логика
├── ViewModels/             # MVVM ViewModels
├── Converters/             # WPF конверторы
└── docs/                   # Документация
```

## Горячие клавиши

- **Ctrl+Enter** - отправить сообщение

## Хранение данных

Диалоги сохраняются в:
```
%AppData%\AiAgent\Conversations\
```

## Устранение неполадок

### Ollama не отвечает
**Проблема**: Ошибка подключения к Ollama  
**Решение**: Убедитесь, что Ollama запущен и доступен на http://localhost:11434

### Нет доступных моделей
**Проблема**: Список моделей пуст  
**Решение**: Загрузите модель командой `ollama pull llama2`

### Ошибки компиляции
**Проблема**: Не найден Newtonsoft.Json  
**Решение**: Восстановите NuGet пакеты в Visual Studio

## Разработчикам

Подробная документация:
- [Architecture.md](docs/Architecture.md) - Архитектура проекта
- [ImplementationPlan.md](docs/ImplementationPlan.md) - План реализации

## Лицензия

MIT License