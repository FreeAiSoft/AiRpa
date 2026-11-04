# Контракт генерации процесса для ИИ-агента (xaml.describe_contract)

Документ для человека; машиночитаемую версию того же контракта отдаёт MCP-инструмент
`xaml.describe_contract` (этап 7 плана `Valid_Xaml_Designer_Plan.md`). Источник правды один —
`AiRpaStudio\Core\Generation\WorkflowAgentContract.cs`: правила, матрица выбора инструмента, примеры и
самокоррекция описаны там, а этот файл обязан совпадать с ним дословно (страж —
`AiRpaStudio.Tests\Generation\AgentContractTests.cs`, тест `Doc_MirrorsContract`).

Зачем контракт: результат работы агента — **валидный XAML, который сразу открывается в конструкторе**.
Инструмент считается успешным только когда файл записан, `ActivityXamlServices.Load` прошёл,
`ActivityValidationServices.Validate` дал 0 ошибок и вкладка конструктора открыта. Поэтому агенту
нельзя «писать XAML», нельзя придумывать активности, типы и имена аргументов, а ошибку гейта нельзя
игнорировать.

## 1. Правила

1. Результат работы — валидный XAML, который сразу открывается в конструкторе: не пиши XAML текстом, не собирай его конкатенацией и не присылай XAML обратно в инструмент.
2. Выражения — только CSharpValue<T> и CSharpReference<T> (в JSON AST — строка C#-кода). Лямбды (x => …) в аргументах запрещены: они не переживают сериализацию и ломают конструктор.
3. Только активности и типы из палитры студии: whitelist отдаёт xaml.create_workflow_from_json с {"describe": true}. Своих активностей, «похожих» имён и произвольных типов не бывает.
4. Входные данные процесса — переменные (JSON: variables[], код: Sequence.Variables); переменная объявляется до использования, тип указывается явно.
5. Имена аргументов — только из палитры (аргумент проверяется по метаданным активности до вставки в конструктор): придуманное имя возвращается ошибкой, а не «молча пустым» свойством.
6. Один процесс — один файл .xaml в проекте (RPA-N\Имя.xaml). Класс процесса, его пространство имён и сборку проекта инструмент выводит сам, переопределять их нужно только при явной необходимости.
7. Persistence и закладки (Bookmark) строятся только через Persist + Pick/Delay в Code-First: блокировка, лямбда или своя активность в дерево не попадают.
8. Конструктор — часть контракта: если после вызова файл не открылся и не прошёл валидацию, вызов считается неуспешным, даже когда файл записан.

## 2. Выбор инструмента

| Ситуация | Инструмент | Почему |
|---|---|---|
| 1–5 простых шагов RPA, точечная правка уже открытого процесса | `xaml.add_activity` (+ `xaml.set_argument`) | минимальный риск: активность и её аргументы проверяются по палитре до вставки в контейнер |
| новый процесс из простых шагов: переменные, присваивания, вывод | `xaml.create_workflow_from_json` | детерминированно: одна строгая схема, один и тот же XAML на одном и том же описании |
| ветвления и циклы без расчётов (If, ForEach<T>, While) | `xaml.create_workflow_from_json` | If/ForEach/While есть в whitelist JSON AST; результата проще достичь описанием, чем кодом |
| вложенные ветвления и циклы с расчётами, InvokeMethod с параметрами | `xaml.create_workflow_from_code` | сложную логику короче выразить кодом; выражения всё равно остаются CSharpValue<T>/CSharpReference<T> |
| TryCatch/Catch, Persist, Bookmark (ожидание внешнего события) | `xaml.create_workflow_from_code` | Catch и Persist не являются активностями палитры — их нельзя выразить JSON AST |
| нужно понять, что уже есть в открытом процессе | `xaml.get_workflow` | сначала снимок дерева, потом правка — иначе шаг вставляется не туда |
| нужны правила, схема, whitelist или пример | `xaml.describe_contract` | контракт и примеры отдаются машиночитаемо: не нужно угадывать формат и имена аргументов |

Полная схема JSON AST — `docs\Workflow_Json_Ast_Schema.md`; справочник инструментов палитры —
`docs\MCP_Workflow_Tools_Guide.md`.

## 3. Few-shot примеры

Каждый пример ниже — не иллюстрация, а **эталон**: он прогнан через настоящий конвейер
(валидация описания/компиляция кода → объектная модель WF → канонический XAML → гейт валидации →
конструктор), поэтому его можно передавать в инструмент как есть.

### sequence-assign — Sequence + Assign (`xaml.create_workflow_from_json`)

```json
{
  "variables": [ { "name": "output", "type": "String" } ],
  "activities": [
    { "type": "Assign", "displayName": "Присваивание",
      "arguments": { "To": "output", "Value": { "value": "готово", "type": "String" } } },
    { "type": "WriteLine", "displayName": "Вывод",
      "arguments": { "Text": "output" } }
  ]
}
```

Почему так: Assign.To — ссылка на переменную (CSharpReference), Assign.Value — литерал через {"value": …}: тип выводится из объявленной переменной, x:Object в XAML не попадает.

### if — If (`xaml.create_workflow_from_json`)

```json
{
  "variables": [ { "name": "counter", "type": "Int32", "default": "0" } ],
  "activities": [
    { "type": "If", "displayName": "Проверка",
      "arguments": { "Condition": "counter > 0" },
      "children": {
        "Then": [ { "type": "WriteLine", "arguments": { "Text": { "value": "не пусто" } } } ],
        "Else": [ { "type": "WriteLine", "arguments": { "Text": { "value": "пусто" } } } ]
      } }
  ]
}
```

Почему так: Condition — строка C#-выражения; ветки Then/Else — массивы активностей, пустая ветка допустима, но лучше её не оставлять.

### foreach — ForEach<T> (`xaml.create_workflow_from_json`)

```json
{
  "variables": [ { "name": "items", "type": "String[]", "default": "new string[0]" } ],
  "activities": [
    { "type": "ForEach", "typeArguments": [ "String" ], "displayName": "Обход",
      "arguments": { "Values": { "expression": "items", "type": "String[]" } },
      "children": {
        "Body": [ { "type": "WriteLine", "arguments": { "Text": "item" } } ]
      } }
  ]
}
```

Почему так: Обобщённая активность требует typeArguments; тело — ActivityAction<T>, элемент цикла доступен по имени item.

### while — While (`xaml.create_workflow_from_json`)

```json
{
  "variables": [ { "name": "counter", "type": "Int32", "default": "0" } ],
  "activities": [
    { "type": "While", "displayName": "Пока счётчик меньше 3",
      "arguments": { "Condition": "counter < 3" },
      "children": {
        "Body": [
          { "type": "Sequence", "displayName": "Тело цикла",
            "children": {
              "Activities": [
                { "type": "Assign", "displayName": "Прибавить",
                  "arguments": { "To": "counter", "Value": "counter + 1" } },
                { "type": "WriteLine", "displayName": "Значение",
                  "arguments": { "Text": "counter.ToString()" } }
              ]
            } }
        ]
      } }
  ]
}
```

Почему так: While.Condition — свойство-выражение (Activity<bool>), поэтому условие задаётся строкой кода; контейнер Body принимает одну активность, поэтому шаги тела обёрнуты в Sequence.

### trycatch — TryCatch (`xaml.create_workflow_from_code`)

```csharp
using System;
using System.Activities;
using System.Activities.Statements;
using Microsoft.CSharp.Activities;

public static class Workflow
{
    public static Activity Build()
    {
        return new TryCatch
        {
            DisplayName = "Обработка ошибок",
            Try = new Sequence
            {
                Activities =
                {
                    new WriteLine
                    {
                        DisplayName = "Работа",
                        Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = "\"шаг выполнен\"" })
                    }
                }
            },
            Catches =
            {
                new Catch<Exception>
                {
                    Action = new ActivityAction<Exception>
                    {
                        Argument = new DelegateInArgument<Exception>("error"),
                        Handler = new WriteLine
                        {
                            DisplayName = "Лог ошибки",
                            Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = "error.Message" })
                        }
                    }
                }
            },
            Finally = new WriteLine
            {
                DisplayName = "Завершение",
                Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = "\"готово\"" })
            }
        };
    }
}
```

Почему так: Catch и TryCatch.Catches не являются активностями палитры: JSON AST их не выражает, поэтому обработка ошибок — только Code-First. У Catch нет DisplayName (это не Activity); вход процесса — public static Activity Build().

### bookmark-persist — Bookmark + Persist (`xaml.create_workflow_from_code`)

```csharp
using System;
using System.Activities;
using System.Activities.Statements;
using Microsoft.CSharp.Activities;

public static class Workflow
{
    public static Activity Build()
    {
        return new Sequence
        {
            DisplayName = "Ожидание внешнего события",
            Activities =
            {
                new WriteLine
                {
                    DisplayName = "Старт",
                    Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = "\"ждём сигнал\"" })
                },
                new Persist
                {
                    DisplayName = "Сохранить состояние"
                },
                new Pick
                {
                    DisplayName = "Ожидание",
                    Branches =
                    {
                        new PickBranch
                        {
                            DisplayName = "Возобновление по таймауту",
                            Trigger = new Delay
                            {
                                Duration = new InArgument<TimeSpan>(
                                    new CSharpValue<TimeSpan> { ExpressionText = "TimeSpan.FromSeconds(30)" })
                            },
                            Action = new WriteLine
                            {
                                DisplayName = "Продолжение",
                                Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = "\"продолжили после паузы\"" })
                            }
                        }
                    }
                }
            }
        };
    }
}
```

Почему так: Persist фиксирует состояние, Pick+Delay даёт точку возобновления (закладку): Persist → перезапуск хоста → Resume. Лямбды и свои активности здесь запрещены — они не сериализуются.

## 4. Самокоррекция

1. ok:false — это задача, а не повод сдаться: прочитай errors[] (с путём до поля) и hints[].
2. Исправляй вход (JSON-описание или код), а не XAML: поле xaml в ответе — диагностика, его нельзя править и присылать назад.
3. Ошибка аргумента — возьми имя и направление аргумента из подсказки палитры; ошибка типа — объяви переменную с нужным типом; «нет активности» — сверься с whitelist.
4. Ошибка «нет ссылки на сборку»/«неизвестный тип» лечится namespaces[]/references[] (JSON) или using (код), а не заменой активности на «похожую».
5. Повтори вызов того же инструмента с исправленным входом; лимит — 3 итерации на один процесс. Если после третьей ошибка осталась, верни пользователю errors[] и hints[] и назови причину.
6. Не понижай требования ради зелёного ответа: не заменяй выражение литералом, не выбрасывай ветку, обработчик или Persist.

## 5. Что проверено тестами

- каждый пример собирается конвейером: `ok:true`, канонический XAML (`x:Class`, `sap2010:ExpressionActivityEditor="C#"`,
  `xmlns:mca`, `TextExpression.ReferencesForImplementation`), файл повторно загружается `ActivityXamlServices.Load`;
- каждый пример открывается в настоящем конструкторе (`DesignerBenchProxy`): активности в дереве,
  валидация без ошибок, нераспознанных активностей и модальных окон нет;
- в JSON-примерах используются только активности и типы палитры (whitelist), в примерах нет лямбд;
- контракт описывает все обязательные сценарии (Sequence+Assign, If, ForEach<T>, While, TryCatch,
  Bookmark+Persist), все инструменты выбора и правило самокоррекции;
- этот документ совпадает с кодом контракта дословно — расхождение валит `Doc_MirrorsContract`.
