# Руководство по использованию add_activity

## Обзор

Инструмент `add_activity` теперь поддерживает добавление активностей в любой контейнер workflow с возможностью указания позиции.

## Новые параметры

### parentActivityId (опционально)
ID родительской активности, в которую нужно добавить новую активность.
- **Не указан**: активность добавляется в `root.Implementation` (обратная совместимость)
- **Указан**: активность добавляется в указанный контейнер

### index (опционально) 
Позиция для вставки активности в коллекции.
- **Не указан**: добавление в конец коллекции
- **0**: вставка в начало
- **Положительное число**: вставка в указанную позицию
- **Больше размера коллекции**: добавление в конец

## Примеры использования

### 1. Простое добавление (обратная совместимость)

Добавление WriteLine в конец root.Implementation:

```json
{
  "activityType": "WriteLine",
  "properties": {
    "DisplayName": "Hello",
    "Text": "Hello World"
  }
}
```

### 2. Добавление в начало workflow

Вставка Delay в начало root.Implementation:

```json
{
  "activityType": "Delay",
  "properties": {
    "DisplayName": "Initial Delay",
    "Duration": "00:00:03"
  },
  "index": 0
}
```

### 3. Добавление в конкретную Sequence

Сначала найдите ID целевой Sequence:

```json
{
  "tool": "find_activity",
  "arguments": {
    "activityType": "Sequence",
    "displayName": "Processing"
  }
}
```

Затем добавьте активность:

```json
{
  "activityType": "WriteLine",
  "properties": {
    "Text": "Processing step"
  },
  "parentActivityId": "sequence-456"
}
```

### 4. Вставка в определенную позицию

Вставка WriteLine в позицию 2 внутри Sequence:

```json
{
  "activityType": "WriteLine",
  "properties": {
    "Text": "Middle step"
  },
  "parentActivityId": "sequence-789",
  "index": 2
}
```

### 5. Добавление в Flowchart

Активность автоматически оборачивается в FlowStep:

```json
{
  "activityType": "WriteLine",
  "properties": {
    "Text": "In flowchart"
  },
  "parentActivityId": "flowchart-123"
}
```

### 6. Создание вложенной структуры

Добавление Sequence с дочерними активностями:

```json
{
  "activityType": "Sequence",
  "properties": {
    "DisplayName": "Nested Sequence"
  },
  "children": [
    {
      "activityType": "WriteLine",
      "properties": {
        "Text": "Step 1"
      }
    },
    {
      "activityType": "Delay",
      "properties": {
        "Duration": "00:00:01"
      }
    },
    {
      "activityType": "WriteLine",
      "properties": {
        "Text": "Step 2"
      }
    }
  ],
  "parentActivityId": "main-sequence",
  "index": 0
}
```

## Рабочий процесс

### Типичный сценарий: добавление активности в середину workflow

1. **Получить структуру workflow**:
   ```json
   {
     "tool": "get_activity_tree"
   }
   ```

2. **Найти нужный контейнер**:
   ```json
   {
     "tool": "find_activity",
     "arguments": {
       "displayName": "Main Processing"
     }
   }
   ```
   Результат: `{"id": "sequence-789", ...}`

3. **Получить дочерние активности** (чтобы понять где вставлять):
   ```json
   {
     "tool": "get_child_activities",
     "arguments": {
       "activityId": "sequence-789"
     }
   }
   ```
   Результат показывает 5 активностей, нужно вставить после 2-й

4. **Добавить новую активность**:
   ```json
   {
     "tool": "add_activity",
     "arguments": {
       "activityType": "WriteLine",
       "properties": {
         "Text": "New step"
       },
       "parentActivityId": "sequence-789",
       "index": 3
     }
   }
   ```

## Поддерживаемые контейнеры

| Контейнер | Свойство коллекции | Примечания |
|-----------|-------------------|------------|
| Sequence | Activities | Стандартный контейнер |
| Parallel | Branches | Параллельные ветки |
| Flowchart | Nodes | Автоматическая обертка в FlowStep |
| ActivityBuilder | Implementation | Корневой элемент (по умолчанию) |

## Обработка ошибок

### Parent not found
```json
{
  "success": false,
  "error": "Parent activity with id 'invalid-id' not found"
}
```
**Решение**: Проверьте ID с помощью `get_activity_tree` или `find_activity`

### Invalid index
```json
{
  "success": false,
  "error": "index must be non-negative"
}
```
**Решение**: Используйте index >= 0

### Container does not support children
```json
{
  "success": false,
  "error": "Target container does not support child activities"
}
```
**Решение**: Активности типа WriteLine, Assign, Delay не могут иметь дочерние элементы

## Успешный результат

```json
{
  "success": true,
  "content": {
    "message": "Activity 'WriteLine' successfully added to workflow",
    "activityType": "WriteLine",
    "displayName": "My WriteLine",
    "id": "writeline-951",
    "parentActivityId": "sequence-789",
    "position": "index 3"
  }
}
```

## Советы по использованию

1. **Используйте `get_activity_tree`** перед добавлением для понимания структуры
2. **Проверяйте ID** с помощью `find_activity` если имя активности известно
3. **Индекс больше размера** безопасен - активность добавится в конец
4. **Для Flowchart** не нужно создавать FlowStep вручную
5. **Обратная совместимость** - старый код продолжит работать

## Связанные инструменты

- [`get_activity_tree`](Mcp/Tools/GetActivityTreeTool.cs) - Получить дерево активностей
- [`find_activity`](Mcp/Tools/FindActivityTool.cs) - Найти активность по критериям
- [`get_child_activities`](Mcp/Tools/GetChildActivitiesTool.cs) - Получить дочерние активности
- [`get_parent_activity`](Mcp/Tools/GetParentActivityTool.cs) - Получить родительскую активность
- [`move_activity`](Mcp/Tools/MoveActivityTool.cs) - Переместить активность
- [`copy_activity`](Mcp/Tools/CopyActivityTool.cs) - Скопировать активность

## Полная документация

См. [`docs/AddActivityTool_Enhancement_Plan.md`](AddActivityTool_Enhancement_Plan.md) для технических деталей и архитектуры.