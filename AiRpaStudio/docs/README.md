# AI Agent Integration для RPA Studio через MCP

## 🎉 Последние обновления

### AI Agent MCP Auto-Discovery (2025-10-20)

**AI агент теперь автоматически видит все доступные MCP инструменты!**

Больше не нужно вручную запрашивать список команд - при старте чата AI получает полную документацию по всем доступным инструментам с примерами использования.

**Что изменилось:**
- ✅ Автоматическая загрузка списка MCP инструментов при инициализации
- ✅ Детальное описание каждого инструмента с параметрами и типами
- ✅ Примеры использования для всех основных команд
- ✅ Системный промпт автоматически включает документацию по инструментам
- ✅ Многоязычная поддержка (русский язык в описаниях)

**Технические детали:**
- Метод `LoadMcpToolsAsync()` автоматически вызывается при инициализации ViewModel
- Генерация контекста через `RpaAiIntegrationService.GenerateAiContextAsync()`
- Включает схемы параметров и примеры JSON команд

**Документация:** [AI MCP Integration Guide](./AI_MCP_Integration_Guide.md)

---

## Обзор проекта

Этот проект добавляет возможность использования AI Agent для создания активностей в RPA Workflow Designer с помощью текстовых команд на естественном языке. Используется стандартный **Model Context Protocol (MCP)** для безопасного и масштабируемого взаимодействия с AI.

## Документация

### Для архитекторов и разработчиков
1. **[MCP_Architecture.md](MCP_Architecture.md)** ⭐ **ОСНОВНОЙ** - MCP Server архитектура
   - Спецификация MCP Tools
   - JSON-RPC протокол
   - Интеграция с Ollama и Claude
   - Безопасность и тестирование

2. **[AI_MCP_Integration_Guide.md](AI_MCP_Integration_Guide.md)** 🆕 - AI-MCP интеграция
   - Автоматическая загрузка инструментов
   - Формат описания для AI
   - Процесс обработки команд
   - Расширение функциональности

3. **[AI_Activity_Integration_Architecture.md](AI_Activity_Integration_Architecture.md)** - Альтернативный подход (прямая интеграция)
   - Диаграммы компонентов
   - Интерфейсы и реализации
   - Последовательность взаимодействий
   - Обработка ошибок

4. **[Implementation_Guide.md](Implementation_Guide.md)** - Руководство по реализации
   - Пошаговый план внедрения
   - Детальные инструкции
   - Timeline и критерии приемки
   - Troubleshooting

### Для конечных пользователей
5. **[User_Guide_AI_Activities.md](User_Guide_AI_Activities.md)** - Руководство пользователя
   - Примеры команд
   - Доступные активности
   - Советы и рекомендации
   - FAQ

## Краткое описание решения

### Что делает система?

Позволяет пользователям добавлять активности в workflow используя обычный текст:
```
Пользователь: "Добавь Sequence с именем 'Главный процесс'"
AI Agent: ✅ Активность Sequence добавлена в workflow
```

### Ключевые компоненты (MCP подход)

1. **RpaMcpServer** - Основной MCP сервер (JSON-RPC 2.0)
2. **MCP Tools** - Набор инструментов для AI:
   - `add_activity` - добавление активностей
   - `list_activities` - список доступных активностей
   - `get_workflow_info` - информация о workflow
   - `remove_activity` - удаление активностей
   - `update_activity` - обновление параметров
   - `save_workflow` - сохранение workflow
3. **ActivityFactory** - Создает экземпляры Activity
4. **OllamaMcpAdapter** - Адаптер для Ollama (не имеет нативной поддержки MCP)
5. **McpServerHost** - Хост для запуска сервера (stdio/HTTP)

### Архитектура с MCP

```
Пользователь
    ↓ текстовый запрос
AiAgentChatControl
    ↓ обработка
AI (Ollama/Claude/GPT)
    ↓ MCP Tool Call
RpaMcpServer
    ↓ execute tool
MCP Tools (add_activity, list_activities и др.)
    ↓ создание + добавление
WorkflowDesigner
    ↓ визуализация
Пользователь видит результат + подтверждение
```

## Текущий статус

✅ **Завершено (фаза Architect):**
- Полная MCP архитектура системы
- Спецификация 6 MCP tools
- Детальные спецификации всех компонентов
- Руководства по реализации и использованию
- Диаграммы и примеры
- Альтернативный подход (прямая интеграция)

⏳ **Следующие шаги (фаза Implementation):**
- Реализация RpaMcpServer (JSON-RPC 2.0)
- Реализация базовых MCP Tools
- Реализация ActivityFactory
- Реализация OllamaMcpAdapter
- Интеграция с CustomWfDesigner
- Тестирование MCP протокола

## Преимущества решения

### Для пользователей
- 🚀 **Быстрая разработка** - создание активностей текстом
- 💡 **Интуитивность** - не нужно помнить, где какая активность
- 🎯 **Точность** - AI понимает намерения и создает правильные структуры
- 📚 **Обучение** - AI объясняет, что делает каждая активность
- 🔗 **Совместимость** - работает с любым AI (Ollama, Claude, GPT)

### Для разработчиков
- 🏗️ **Стандартизация** - использование MCP протокола
- 🔒 **Безопасность** - централизованный контроль доступа
- 🧪 **Тестируемость** - каждый tool - отдельный компонент
- 🔧 **Расширяемость** - легко добавлять новые tools
- 📖 **Самодокументирование** - MCP tools автоматически документируются
- 🌍 **Экосистема** - совместимость с MCP ecosystem

## Технический стек

- **.NET Framework 4.7.2+**
- **System.Activities** (Workflow Foundation)
- **WPF** для UI
- **Model Context Protocol (MCP)** - стандартный протокол для AI
- **JSON-RPC 2.0** для MCP коммуникации
- **Ollama** для локального AI (с адаптером)
- **Newtonsoft.Json** для JSON обработки
- **AvalonDock** для layout

## Поддерживаемые активности

### Control Flow
Sequence, If, While, DoWhile, ForEach, Switch

### Flowchart
Flowchart, FlowDecision, FlowSwitch

### State Machine
StateMachine, State, FinalState

### Primitives
Assign, Delay, InvokeMethod, WriteLine

### Collection
AddToCollection, ClearCollection, ExistsInCollection, RemoveFromCollection

### Error Handling
TryCatch, Throw, Rethrow

### Browser & Flow
Активности из Activities.Browser и Activities.Flow

## Примеры использования

### Простая активность
```
Пользователь: Добавь WriteLine с текстом "Hello, World!"
AI: ✅ Добавлена активность WriteLine в workflow
```

### Вложенные активности
```
Пользователь: Создай Sequence с тремя WriteLine:
1. "Начало"
2. "Середина"  
3. "Конец"

AI: ✅ Добавлена Sequence с 3 вложенными WriteLine
```

### Сложная структура
```
Пользователь: Создай процесс с обработкой ошибок:
- TryCatch блок
- В Try: прочитать файл
- В Catch: залогировать ошибку

AI: ✅ Создана структура TryCatch с обработкой ошибок
```

## Timeline реализации

| Фаза | Продолжительность | Статус |
|------|-------------------|--------|
| Architect (проектирование) | 1 день | ✅ Завершено |
| MCP Server Core | 2-3 дня | ⏳ Планируется |
| MCP Tools (базовые) | 3-4 дня | ⏳ Планируется |
| MCP Tools (доп.) | 2-3 дня | ⏳ Планируется |
| Ollama Adapter | 1-2 дня | ⏳ Планируется |
| Testing (тестирование) | 2-3 дня | ⏳ Планируется |
| Documentation (документация) | 1 день | ✅ Завершено |
| **Итого** | **12-19 дней** | |

## Метрики успеха

- **Точность парсинга команд**: > 95%
- **Успешное добавление активностей**: > 90%
- **Время отклика**: < 5 секунд
- **Покрытие тестами**: > 80%
- **Удовлетворенность пользователей**: > 4/5

## Риски и митигация

### Риск 1: Ollama не поддерживает MCP нативно
**Митигация:**
- Создание OllamaMcpAdapter
- Промежуточный слой для эмуляции MCP
- Альтернатива: использование Claude Desktop с MCP

### Риск 2: Сложность MCP протокола
**Митигация:**
- Использование существующих MCP библиотек
- Детальная документация
- Примеры и тесты

### Риск 3: Производительность JSON-RPC
**Митигация:**
- Асинхронные операции
- Кэширование tool schemas
- Оптимизация сериализации

## Будущие улучшения (Roadmap)

### Фаза 2 (после базовой реализации)
- Редактирование существующих активностей
- Удаление активностей
- Поиск и замена

### Фаза 3 (расширенные возможности)
- Генерация целых процессов по описанию
- Анализ workflow и предложение улучшений
- Автоматическое тестирование созданных процессов

### Фаза 4 (интеграция)
- Поддержка custom активностей
- Интеграция с системами контроля версий
- Совместная работа через AI

## Как начать разработку

1. **Изучить MCP архитектуру** ⭐ **НАЧНИ ЗДЕСЬ**
   ```bash
   читать: docs/MCP_Architecture.md
   ```

2. **Ознакомиться с MCP спецификацией**
   - [Model Context Protocol Spec](https://modelcontextprotocol.io/)
   - [MCP TypeScript SDK](https://github.com/modelcontextprotocol/typescript-sdk)

3. **Следовать руководству по реализации**
   ```bash
   читать: docs/Implementation_Guide.md
   ```

4. **Начать с базовых компонентов**
   - RpaMcpServer (JSON-RPC 2.0)
   - Базовые MCP Tools (add_activity, list_activities)
   - ActivityFactory
   - OllamaMcpAdapter

5. **Тестировать MCP протокол**
   - Unit тесты для каждого tool
   - Integration тесты для JSON-RPC
   - Тесты с реальным AI

6. **Использовать режим Code в Roo**
   ```
   переключиться в mode: code
   реализовать согласно MCP спецификации
   ```

## Контакты и поддержка

### Документация
- **MCP Архитектура**: [MCP_Architecture.md](MCP_Architecture.md) ⭐
- Альтернативная архитектура: [AI_Activity_Integration_Architecture.md](AI_Activity_Integration_Architecture.md)
- Реализация: [Implementation_Guide.md](Implementation_Guide.md)
- Использование: [User_Guide_AI_Activities.md](User_Guide_AI_Activities.md)

### Внешние ресурсы
- **[Model Context Protocol](https://modelcontextprotocol.io/)** - официальная спецификация MCP
- [MCP TypeScript SDK](https://github.com/modelcontextprotocol/typescript-sdk)
- [Workflow Foundation Documentation](https://learn.microsoft.com/en-us/dotnet/framework/windows-workflow-foundation/)
- [Ollama API Documentation](https://github.com/ollama/ollama/blob/main/docs/api.md)
- [System.Activities API Reference](https://learn.microsoft.com/en-us/dotnet/api/system.activities)

## Лицензия

Проект использует те же условия лицензирования, что и основной RPA проект.

## Changelog

### Version 1.0 (Planned)
- MCP Server с базовыми tools
- Поддержка основных типов активностей
- Интеграция с Ollama через адаптер
- Поддержка Claude Desktop
- Система безопасности и audit log

### Version 0.1 (Current - Architecture)
- Полная MCP архитектура системы
- Спецификация 6 MCP tools
- Альтернативный подход (прямая интеграция)
- Руководства по реализации и использованию

---

**Статус проекта:** 🎨 Architecture Complete (MCP-based) → 🚀 Ready for Implementation

**Архитектурный подход:** Model Context Protocol (MCP) - стандартный протокол для AI интеграции

**Последнее обновление:** 2025-10-13

**Следующий шаг:** Переключиться в режим Code и начать реализацию MCP сервера