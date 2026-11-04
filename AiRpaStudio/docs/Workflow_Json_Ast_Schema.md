# JSON AST рабочего процесса (xaml.create_workflow_from_json)

Справочник по JSON-описанию процесса для ИИ-агента. Реализация — этап 6 плана
`Valid_Xaml_Designer_Plan.md`: строгая схема проверяется **до** построения модели WF, затем описание
превращается в объектную модель и проходит общий конвейер (канонизация → сериализатор WF → гейт
валидации → вкладка конструктора).

Агенту **не нужно** писать XAML: строки в описании — это C#-выражения, а не разметка.

## 1. Инструмент

`xaml.create_workflow_from_json`

| Аргумент | Обяз. | Смысл |
|---|---|---|
| `path` | да | путь к файлу процесса (`.xaml`) |
| `ast` | да | описание процесса: JSON-объект или JSON-строка |
| `project` | нет | имя сборки проекта (`RPA-1`); по умолчанию — из описания или из имени папки файла |
| `namespace` | нет | пространство имён класса процесса (`RPA_1`) |
| `className` | нет | имя класса процесса (по умолчанию — имя файла без расширения) |
| `describe` | нет | `true` — вернуть схему, whitelist активностей и типов, пример (ничего не собирает) |

Ответ: `{ok, workflowId, filePath, activityCount, opened, activated, variables[], namespaces[],
references[], errors[], warnings[], hints[]}`. Собранный XAML возвращается **только** при ошибке — его
нельзя править и присылать назад.

## 2. Документ процесса

```json
{
  "project": "RPA-1",
  "name": "Main",
  "namespace": "RPA_1",
  "description": "необязательный комментарий",
  "variables": [ { "name": "output", "type": "String" } ],
  "namespaces": [ "System.Text.RegularExpressions" ],
  "references": [ "System.Text.RegularExpressions" ],
  "activities": [ /* шаги; корнем станет Sequence */ ]
}
```

- `activities` **или** `root` (ровно одно из двух): `root` — явная корневая активность, `activities` —
  список шагов, который заворачивается в корневой `Sequence`.
- `variables` — переменные процесса; попадают в `Sequence.Variables` (для формы `activities`).
- `namespaces`/`references` — то, что нужно выражениям (`using` и сборки); попадают
  в `TextExpression.NamespacesForImplementation`/`ReferencesForImplementation`.
- Неизвестное поле — ошибка: схема строгая, опечатка не теряется молча.

## 3. Активность

```json
{
  "type": "If",
  "displayName": "Проверка",
  "typeArguments": [ "String" ],
  "arguments": { "Condition": "counter > 0" },
  "children": { "Then": [ /* ... */ ], "Else": [ /* ... */ ] },
  "variables": [ { "name": "item", "type": "String" } ]
}
```

| Поле | Обяз. | Смысл |
|---|---|---|
| `type` | да | имя активности палитры (`Sequence`, `If`, `ForEach`, `WriteLine`, `Assign`, …) |
| `displayName` | нет | подпись на диаграмме |
| `typeArguments` | для обобщённых | аргументы типа: `["String"]` для `ForEach<T>` |
| `arguments` | нет | значения аргументов по именам из палитры |
| `children` | нет | вложенные активности по имени контейнера (`Activities`, `Then`, `Else`, `Body`) |
| `variables` | нет | переменные контейнера; только у активностей с коллекцией `Variables` (`Sequence`, `Parallel`) |

Допустимые имена активностей, их аргументы и контейнеры — из палитры студии: точный список отдаёт
`describe: true` (`activities`). Узлы блок-схемы (`Flowchart`) в JSON AST не поддерживаются — нужны
`Sequence`/`If`/`While`.

## 4. Значение аргумента

| Форма | Смысл |
|---|---|
| `"output"` | **C#-выражение** (строка кода), становится `CSharpValue<T>`/`CSharpReference<T>` |
| `{ "expression": "counter + 1", "type": "Int32" }` | выражение с явным типом |
| `{ "value": "готово", "type": "String" }` | литерал (константа), а не выражение |
| `{ "value": 5 }` | числовой литерал |
| `{"value": null}` | литерал `null` |

Правила: строка — всегда выражение (литерал задаётся явно через `value`); аргумент-ссылка
(`Out`/`InOut`, например `Assign.To`) принимает только `expression`; лямбды запрещены — только
`CSharpValue<T>`/`CSharpReference<T>`.

### 4.1. Особый случай `Assign`

У `Assign` аргументы `To`/`Value` необобщённые, тип значения определяет выражение. Тип выводится
автоматически из объявленной переменной или из соседнего аргумента:

```json
{ "type": "Assign", "arguments": { "To": "output", "Value": "\"готово\"" } }
```

Если вывести тип не из чего (переменная не объявлена, тип не указан) — ошибка запроса: молчаливый
`x:Object` в XAML недопустим. Явная форма: `"To": { "expression": "output", "type": "String" }`.

## 5. Переменная

```json
{ "name": "counter", "type": "Int32", "default": "0" }
```

- `name` — идентификатор C# (латиница, цифры, `_`; не с цифры), уникальный в области видимости;
- `type` — только из whitelist (`String`, `Int32`, `Int64`, `Double`, `Decimal`, `Boolean`, `DateTime`,
  `TimeSpan`, `Object`, `String[]`, `List<String>`), полный список — `describe: true` (`types`);
- `default` — необязательное значение по умолчанию: выражение или `{ "value": ... }`.

## 6. Пример: ветвление и цикл

```json
{
  "variables": [ { "name": "counter", "type": "Int32", "default": "0" } ],
  "activities": [
    { "type": "Assign", "displayName": "Счётчик",
      "arguments": { "To": "counter", "Value": "1" } },
    { "type": "If", "displayName": "Проверка",
      "arguments": { "Condition": "counter > 0" },
      "children": {
        "Then": [ { "type": "WriteLine", "arguments": { "Text": "\"не пусто\"" } } ],
        "Else": [ { "type": "WriteLine", "arguments": { "Text": "\"пусто\"" } } ]
      } },
    { "type": "ForEach", "typeArguments": [ "String" ], "displayName": "Обход",
      "arguments": { "Values": { "expression": "new string[0]", "type": "String[]" } },
      "children": {
        "Body": [ { "type": "WriteLine", "arguments": { "Text": "\"элемент\"" } } ]
      } }
  ]
}
```

## 7. Ограничения и самокоррекция

- лимиты: размер описания ≤ 200 КБ, вложенность ≤ 32, активностей ≤ 500;
- ошибки возвращаются списком, каждая — с путём до поля (`$.activities[0].children.Then[1].type`)
  и подсказкой (доступные активности, типы, допустимые поля);
- при любой ошибке файл процесса **не** записывается: описание правится и присылается заново;
- успешный ответ означает, что файл записан, XAML прошёл гейт (`ActivityXamlServices.Load` +
  `ActivityValidationServices.Validate` = 0 ошибок) и вкладка конструктора открыта.
- полный контракт для агента (правила, выбор инструмента, few-shot примеры, самокоррекция) — инструмент `xaml.describe_contract` и документ `docs\Workflow_Agent_Contract.md`.
