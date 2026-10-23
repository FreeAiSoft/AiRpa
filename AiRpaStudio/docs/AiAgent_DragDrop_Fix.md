# Исправление ошибок при перетаскивании AI Agent контрола

## Проблема

При перетаскивании контрола [`AiAgentChatControl`](../AiAgent/Control/AiAgentChatControl.xaml.cs) между панелями в AvalonDock возникали две ошибки:

1. **NullReferenceException** в `System.Windows.Input.KeyboardInputProvider`
2. **ArgumentException** "Нулевой дескриптор Hwnd не является допустимым"

### Причина

Ошибки возникали из-за того, что контрол пытался:
- Установить фокус на [`InputTextBox`](../AiAgent/Control/AiAgentChatControl.xaml:279) при загрузке
- Прокручивать [`MessageScrollViewer`](../AiAgent/Control/AiAgentChatControl.xaml:228) при добавлении сообщений

Во время перетаскивания в AvalonDock контрол временно отключается от визуального дерева, что делает эти операции невозможными и вызывает исключения.

## Решение

Добавлены защитные механизмы для безопасного выполнения UI операций:

### 1. Безопасная установка фокуса

Создан метод `SafeSetFocus()`, который:
- Проверяет, что контрол загружен и видим
- Проверяет доступность `InputTextBox`
- Использует `Dispatcher.BeginInvoke` для асинхронной установки фокуса
- Оборачивает операцию в `try-catch` для подавления ошибок при drag-and-drop

```csharp
private void SafeSetFocus()
{
    try
    {
        if (IsLoaded && IsVisible && InputTextBox != null)
        {
            _ = Dispatcher.BeginInvoke(new System.Action(() =>
            {
                try
                {
                    if (InputTextBox.IsVisible && InputTextBox.IsEnabled)
                    {
                        InputTextBox.Focus();
                    }
                }
                catch
                {
                    // Игнорируем ошибки фокуса при drag-and-drop операциях
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }
    catch
    {
        // Игнорируем ошибки при установке фокуса во время перетаскивания
    }
}
```

### 2. Безопасная прокрутка сообщений

Создан метод `SafeScrollToEnd()`, который:
- Проверяет доступность `MessageScrollViewer`
- Проверяет, что контрол видим и загружен
- Оборачивает прокрутку в `try-catch`

```csharp
private void SafeScrollToEnd()
{
    try
    {
        if (IsLoaded && IsVisible && MessageScrollViewer != null && MessageScrollViewer.IsVisible)
        {
            MessageScrollViewer.ScrollToEnd();
        }
    }
    catch
    {
        // Игнорируем ошибки прокрутки при drag-and-drop операциях
    }
}
```

## Результат

После внедрения исправлений:
- ✅ Контрол перетаскивается между панелями без ошибок
- ✅ Функциональность полностью сохранена
- ✅ Фокус устанавливается при нормальной загрузке контрола
- ✅ Автопрокрутка сообщений работает корректно

## Затронутые файлы

- [`AiAgent/Control/AiAgentChatControl.xaml.cs`](../AiAgent/Control/AiAgentChatControl.xaml.cs) - добавлены методы безопасности

## Примечания

Данный подход применим для любых WPF контролов, используемых в AvalonDock, которые выполняют UI операции при загрузке или в event handlers. Ключевые моменты:

1. Проверять `IsLoaded` и `IsVisible` перед UI операциями
2. Использовать `Dispatcher.BeginInvoke` для асинхронных операций
3. Оборачивать критические операции в `try-catch`
4. Проверять доступность UI элементов перед взаимодействием с ними