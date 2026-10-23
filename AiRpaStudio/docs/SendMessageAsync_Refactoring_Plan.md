# План рефакторинга метода SendMessageAsync()

## Проблема

Текущая реализация [`SendMessageAsync()`](../AiAgent/ViewModels/AiAgentChatViewModel.cs:565) содержит **корявую псевдо-рекурсию** с жестко зашитыми уровнями вложенности (LEVEL1, LEVEL2, LEVEL3):

### Основные проблемы:

1. **Массивное дублирование кода** (~700 строк из 700)
   - Логика отправки AI запроса повторяется 3+ раз
   - Логика обработки результата MCP команды дублируется
   - Обработка стриминга контента идентична в каждом блоке

2. **Жесткое ограничение глубины**
   - Поддерживается только 3 уровня вложенности
   - После LEVEL3 процесс останавливается, даже если есть еще команды

3. **Сложность поддержки**
   - Любое изменение нужно применять в 3+ местах
   - Высокий риск ошибок при модификации

4. **Неоптимальная структура**
   - Вложенные try-catch блоки
   - Множественные проверки одних и тех же условий
   - Сложная логика отслеживания состояния

## Предлагаемое решение

### Архитектура

Заменить псевдо-рекурсию на **итеративный подход** с использованием цикла:

```
┌─────────────────────────────────────────────────────────┐
│ 1. Пользователь отправляет сообщение                    │
└────────────────┬────────────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────────┐
│ 2. Получаем первый ответ от AI                          │
└────────────────┬────────────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────────┐
│ 3. Цикл обработки команд (макс. N итераций)             │
│                                                          │
│  ┌──────────────────────────────────────────────┐       │
│  │ 3.1 Проверяем наличие MCP команд в ответе   │       │
│  └──────┬───────────────────────────────────────┘       │
│         │                                               │
│         │ Нет команд                                    │
│         ├──────────────────► ВЫХОД ИЗ ЦИКЛА            │
│         │                                               │
│         │ Есть команды                                  │
│         ▼                                               │
│  ┌──────────────────────────────────────────────┐       │
│  │ 3.2 Выполняем команду через MCP              │       │
│  └──────┬───────────────────────────────────────┘       │
│         │                                               │
│         ▼                                               │
│  ┌──────────────────────────────────────────────┐       │
│  │ 3.3 Добавляем результат в чат                │       │
│  │     (успех или ошибка)                       │       │
│  └──────┬───────────────────────────────────────┘       │
│         │                                               │
│         ▼                                               │
│  ┌──────────────────────────────────────────────┐       │
│  │ 3.4 Отправляем результат обратно AI          │       │
│  │     Получаем новый ответ (streaming)         │       │
│  └──────┬───────────────────────────────────────┘       │
│         │                                               │
│         └────────► Возврат к 3.1                        │
│                   (следующая итерация)                  │
└─────────────────────────────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────────┐
│ 4. Завершение: автосохранение, очистка ресурсов         │
└─────────────────────────────────────────────────────────┘
```

### Структура рефакторинга

#### 1. Вспомогательный класс для контекста итерации

```csharp
private class AiInteractionContext
{
    public int IterationCount { get; set; }
    public const int MaxIterations = 10; // Защита от бесконечных циклов
    public bool ShouldContinue => IterationCount < MaxIterations;
}
```

#### 2. Метод для отправки сообщения AI (с streaming)

```csharp
/// <summary>
/// Отправляет сообщение AI и получает ответ с потоковой передачей
/// </summary>
/// <param name="role">Роль сообщения (user/assistant)</param>
/// <param name="updateInterval">Интервал обновления UI</param>
/// <returns>Созданное сообщение ассистента с полным содержимым</returns>
private async Task<ChatMessage> SendToAiAndStreamResponseAsync(
    TimeSpan updateInterval)
{
    // 1. Создаем сообщение для ответа AI
    // 2. Настраиваем streaming
    // 3. Вызываем _currentProvider.ChatStreamAsync()
    // 4. Обновляем UI асинхронно
    // 5. Возвращаем готовое сообщение
}
```

#### 3. Метод для обработки результата MCP команды

```csharp
/// <summary>
/// Обрабатывает результат выполнения MCP команды
/// </summary>
/// <param name="result">Результат операции RPA</param>
/// <returns>True если нужно продолжить обработку, False если выход</returns>
private bool ProcessMcpCommandResult(RpaOperationResult result)
{
    // 1. Проверяем наличие результатов или ошибок
    // 2. Создаем и добавляем сообщение в чат
    // 3. Возвращаем флаг продолжения
}
```

#### 4. Основной метод SendMessageAsync() - упрощенная версия

```csharp
private async Task SendMessageAsync()
{
    // === ЭТАП 1: Валидация и подготовка ===
    if (string.IsNullOrWhiteSpace(UserInput) || SelectedModel == null)
        return;

    // Добавляем сообщение пользователя
    // Очищаем поле ввода
    IsSending = true;
    
    try
    {
        _cancellationTokenSource = new CancellationTokenSource();
        TimeSpan updateInterval = TimeSpan.FromMilliseconds(50);

        // === ЭТАП 2: Первый ответ от AI ===
        ChatMessage firstResponse = await SendToAiAndStreamResponseAsync(updateInterval);
        CurrentConversation.Messages.Add(firstResponse);

        // === ЭТАП 3: Цикл обработки MCP команд ===
        var context = new AiInteractionContext();
        ChatMessage currentAiResponse = firstResponse;

        while (context.ShouldContinue)
        {
            context.IterationCount++;
            Debug.WriteLine($"[MCP-ITERATION-{context.IterationCount}] Processing AI response...");

            // 3.1 Проверяем и выполняем MCP команды
            RpaOperationResult mcpResult = await _rpaIntegration.ProcessAiResponseAsync(
                currentAiResponse.Content,
                IsToolApproved
            );

            // 3.2 Если нет команд или выполнение не требует продолжения - выходим
            if (!ProcessMcpCommandResult(mcpResult))
            {
                Debug.WriteLine($"[MCP-ITERATION-{context.IterationCount}] No more commands, exiting loop");
                break;
            }

            // 3.3 Отправляем результат обратно AI и получаем новый ответ
            currentAiResponse = await SendToAiAndStreamResponseAsync(updateInterval);
            CurrentConversation.Messages.Add(currentAiResponse);
        }

        if (context.IterationCount >= AiInteractionContext.MaxIterations)
        {
            Debug.WriteLine($"[MCP-WARNING] Reached max iterations limit ({AiInteractionContext.MaxIterations})");
        }

        StatusMessage = "Ответ получен";

        // === ЭТАП 4: Автосохранение ===
        if (AutoSaveConversations)
        {
            await SaveConversationAsync();
        }
    }
    catch (OperationCanceledException)
    {
        // Обработка отмены
    }
    catch (Exception ex)
    {
        // Обработка ошибок
    }
    finally
    {
        IsSending = false;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }
}
```

## Преимущества нового подхода

### 1. Уменьшение объема кода
- **Было:** ~700 строк с дублированием
- **Будет:** ~200 строк чистого кода

### 2. Гибкость
- Легко изменить максимальное количество итераций
- Можно добавить дополнительные условия выхода
- Простая настройка поведения через параметры

### 3. Поддерживаемость
- Изменения в одном месте применяются ко всем итерациям
- Ясная и понятная логика
- Легко добавлять новые возможности

### 4. Отладка и логирование
- Четкая нумерация итераций
- Понятная трассировка выполнения
- Упрощенная диагностика проблем

### 5. Безопасность
- Защита от бесконечных циклов через MaxIterations
- Централизованная обработка ошибок
- Контролируемое завершение процесса

## Детальная реализация

### SendToAiAndStreamResponseAsync()

```csharp
/// <summary>
/// Отправляет текущую историю сообщений AI и получает ответ с streaming
/// </summary>
private async Task<ChatMessage> SendToAiAndStreamResponseAsync(TimeSpan updateInterval)
{
    // Создаем сообщение для ответа AI
    ChatMessage assistantMessage = new ChatMessage
    {
        Role = "assistant",
        Content = string.Empty,
        Timestamp = DateTime.Now,
        IsStreaming = true
    };
    Messages.Add(assistantMessage);

    // Буфер для накопления чанков
    StringBuilder buffer = new StringBuilder();
    DateTime lastUpdateTime = DateTime.Now;

    // Подготавливаем сообщения для отправки
    List<ChatMessage> messagesToSend = Messages.Where(m => !m.IsStreaming).ToList();
    if (!string.IsNullOrEmpty(SystemPrompt))
    {
        messagesToSend.Insert(0, new ChatMessage
        {
            Role = "system",
            Content = SystemPrompt
        });
    }

    // Получаем ответ с потоковой передачей
    await _currentProvider.ChatStreamAsync(
        SelectedModel.Id,
        messagesToSend,
        chunk =>
        {
            lock (buffer)
            {
                buffer.Append(chunk);
            }

            // Обновляем UI с интервалом
            TimeSpan timeSinceLastUpdate = DateTime.Now - lastUpdateTime;
            if (timeSinceLastUpdate >= updateInterval)
            {
                lastUpdateTime = DateTime.Now;
                string contentToAdd;
                lock (buffer)
                {
                    contentToAdd = buffer.ToString();
                    buffer.Clear();
                }

                Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() =>
                    {
                        assistantMessage.Content += contentToAdd;
                    })
                );
            }
        },
        usageInfo =>
        {
            // Обновляем статистику использования
            Application.Current.Dispatcher.Invoke(() =>
            {
                SessionPromptTokens += usageInfo.PromptTokens;
                SessionCompletionTokens += usageInfo.CompletionTokens;

                if (SelectedModel?.PromptPrice.HasValue == true && 
                    SelectedModel?.CompletionPrice.HasValue == true)
                {
                    decimal promptCost = usageInfo.PromptTokens * SelectedModel.PromptPrice.Value / 1000000m;
                    decimal completionCost = usageInfo.CompletionTokens * SelectedModel.CompletionPrice.Value / 1000000m;
                    SessionCost += promptCost + completionCost;
                }
            });
        },
        _cancellationTokenSource.Token
    );

    // Обновляем оставшиеся данные из буфера
    string remainingContent;
    lock (buffer)
    {
        remainingContent = buffer.ToString();
        buffer.Clear();
    }

    if (!string.IsNullOrEmpty(remainingContent))
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            assistantMessage.Content += remainingContent;
        });
    }

    assistantMessage.IsStreaming = false;
    return assistantMessage;
}
```

### ProcessMcpCommandResult()

```csharp
/// <summary>
/// Обрабатывает результат выполнения MCP команды и добавляет сообщение в чат
/// </summary>
/// <returns>True если нужно продолжить цикл (отправить результат AI), False если выход</returns>
private bool ProcessMcpCommandResult(RpaOperationResult result)
{
    try
    {
        Debug.WriteLine($"[MCP] ProcessResult: Success={result.Success}, ExecutedCommands={result.ExecutedCommands.Count}, Errors={result.Errors.Count}");

        // Если нет команд и нет ошибок - выходим
        if (result.ExecutedCommands.Count == 0 && result.Errors.Count == 0)
        {
            return false;
        }

        // Создаем сообщение с результатом
        ChatMessage resultMessage = new ChatMessage
        {
            Role = "user",
            Content = result.Success
                ? $"✅ Результат выполнения команды:\n{string.Join("\n", result.ExecutedCommands)}"
                : $"❌ {result.Message}\n{string.Join("\n", result.Errors)}",
            Timestamp = DateTime.Now,
            Type = MessageType.McpCommand
        };

        Messages.Add(resultMessage);
        CurrentConversation.Messages.Add(resultMessage);

        Debug.WriteLine($"[MCP] Added result message to chat");
        return true; // Продолжаем цикл - отправляем результат AI
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[MCP-ERROR] ProcessMcpCommandResult exception: {ex.Message}");
        
        // Добавляем сообщение об ошибке
        ChatMessage errorMessage = new ChatMessage
        {
            Role = "system",
            Content = $"❌ Ошибка обработки результата команды: {ex.Message}",
            Timestamp = DateTime.Now,
            Type = MessageType.McpCommand
        };
        Messages.Add(errorMessage);
        CurrentConversation.Messages.Add(errorMessage);
        
        return false; // Прерываем цикл при критической ошибке
    }
}
```

## План миграции

### Этап 1: Создание вспомогательных методов
- [x] Создать `AiInteractionContext`
- [ ] Реализовать `SendToAiAndStreamResponseAsync()`
- [ ] Реализовать `ProcessMcpCommandResult()`

### Этап 2: Рефакторинг основного метода
- [ ] Создать новую версию `SendMessageAsync()` с циклом
- [ ] Сохранить старую версию как `SendMessageAsync_OLD()` для тестирования

### Этап 3: Тестирование
- [ ] Тест: простой запрос без MCP команд
- [ ] Тест: одна MCP команда
- [ ] Тест: несколько последовательных команд
- [ ] Тест: команда с ошибкой
- [ ] Тест: команда с исправлением (error recovery)
- [ ] Тест: достижение лимита итераций
- [ ] Тест: отмена операции пользователем

### Этап 4: Финализация
- [ ] Удалить старый код `SendMessageAsync_OLD()`
- [ ] Обновить документацию
- [ ] Провести code review

## Дополнительные улучшения

### 1. Настраиваемый лимит итераций
Добавить в настройки приложения:
```csharp
public int MaxMcpIterations { get; set; } = 10;
```

### 2. Метрики и мониторинг
```csharp
public int TotalMcpCommandsExecuted { get; set; }
public int CurrentIterationDepth { get; set; }
```

### 3. Умная остановка
Добавить анализ ответа AI для определения завершения:
```csharp
private bool IsAiFinalResponse(string content)
{
    // Проверяем маркеры финального ответа
    // Например: "задача выполнена", "готово", и т.д.
}
```

### 4. Обработка ошибок с retry
```csharp
private async Task<RpaOperationResult> ProcessAiResponseWithRetryAsync(
    string aiResponse, 
    int maxRetries = 3)
{
    // Реализация retry логики для временных ошибок
}
```

## Заключение

Предложенный рефакторинг решает все выявленные проблемы:

✅ Устраняет дублирование кода  
✅ Убирает жесткие ограничения на глубину  
✅ Улучшает читаемость и поддерживаемость  
✅ Добавляет защиту от бесконечных циклов  
✅ Упрощает добавление новых возможностей  
✅ Улучшает отладку и логирование  

Новый подход использует проверенный паттерн **итеративной обработки**, который является стандартным решением для подобных задач в enterprise-системах.