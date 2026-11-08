using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Форма примера в контракте: JSON AST (<c>xaml.create_workflow_from_json</c>) или код агента
    /// (<c>xaml.create_workflow_from_code</c>). Форма определяет и инструмент, и способ проверки
    /// примера в тестах — пример без проверки конвейером в контракт не попадает.
    /// </summary>
    internal enum WorkflowAgentExampleFormat
    {
        /// <summary>Готовое описание процесса для инструмента JSON AST.</summary>
        Json,

        /// <summary>C#-код агента для инструмента Code-First.</summary>
        CodeFirst
    }

    /// <summary>Few-shot пример контракта: сценарий, инструмент и проверяемые конвейером данные.</summary>
    internal sealed class WorkflowAgentExample
    {
        internal WorkflowAgentExample(
            string name,
            string scenario,
            WorkflowAgentExampleFormat format,
            string payload,
            string notes)
        {
            Name = name;
            Scenario = scenario;
            Format = format;
            Payload = payload;
            Notes = notes;
        }

        /// <summary>Короткое имя примера (оно же — имя раздела в документации).</summary>
        internal string Name { get; }

        /// <summary>Что показывает пример (<c>Sequence + Assign</c>, <c>If</c>, …).</summary>
        internal string Scenario { get; }

        internal WorkflowAgentExampleFormat Format { get; }

        /// <summary>Тело примера: JSON-описание процесса или C#-код — ровно то, что агент передаёт в инструмент.</summary>
        internal string Payload { get; }

        /// <summary>Подсказка «почему именно так» — то, что агент должен усвоить из примера.</summary>
        internal string Notes { get; }

        /// <summary>Инструмент, которым пример проверяется и которым агент его повторит.</summary>
        internal string ToolName =>
            Format == WorkflowAgentExampleFormat.Json ? ContractToolNames.Json : ContractToolNames.CodeFirst;

        /// <summary>Формат для агента: <c>json</c> или <c>csharp</c>.</summary>
        internal string FormatName => Format == WorkflowAgentExampleFormat.Json ? "json" : "csharp";
    }

    /// <summary>Строка матрицы выбора инструмента: ситуация → инструмент → причина.</summary>
    internal sealed class WorkflowAgentToolChoice
    {
        internal WorkflowAgentToolChoice(string situation, string tool, string reason)
        {
            Situation = situation;
            Tool = tool;
            Reason = reason;
        }

        internal string Situation { get; }

        internal string Tool { get; }

        internal string Reason { get; }
    }

    /// <summary>Имена инструментов, на которые ссылается контракт (не строки «в воздухе»).</summary>
    internal static class ContractToolNames
    {
        internal const string Palette = "xaml.add_activity";
        internal const string SetArgument = "xaml.set_argument";
        internal const string GetWorkflow = "xaml.get_workflow";
        internal const string CodeFirst = "xaml.create_workflow_from_code";
        internal const string Json = "xaml.create_workflow_from_json";
        internal const string Describe = "xaml.describe_contract";
    }

    /// <summary>
    /// Контракт инструментов генерации процесса для ИИ-агента (этап 7 плана
    /// конвейер генерации XAML).
    /// <para>
    /// Одно место, где описано: (1) жёсткие правила генерации (только <c>CSharpValue&lt;T&gt;</c>/
    /// <c>CSharpReference&lt;T&gt;</c>, только активности и типы палитры, никакого «текста XAML»),
    /// (2) выбор инструмента (палитра / Code-First / JSON AST), (3) few-shot примеры и
    /// (4) правило самокоррекции: ошибка гейта — это задача для агента, а не повод сдаться.
    /// </para>
    /// <para>
    /// Контракт читают двое: агент — через инструмент <c>xaml.describe_contract</c> (и через текст
    /// из <see cref="Describe"/>), человек — через <c>dev-dev-docs\Workflow_Agent_Contract.md</c>.
    /// Каждый пример проверен прогоном через настоящий конвейер
    /// (<see cref="JsonWorkflowService"/>/<see cref="CodeFirstWorkflowService"/> → гейт), поэтому
    /// «пример из документации не собирается» невозможен.
    /// </para>
    /// </summary>
    internal static class WorkflowAgentContract
    {
        /// <summary>Версия контракта: агент видит, что правила менялись, и не смешивает их.</summary>
        internal const string Version = "ai-rpa.xaml-contract/1";

        /// <summary>Жёсткие правила генерации. Правило без отражения в коде/гейте сюда не попадает.</summary>
        internal static readonly string[] Rules =
        {
            "Результат работы — валидный XAML, который сразу открывается в конструкторе: не пиши XAML текстом, " +
            "не собирай его конкатенацией и не присылай XAML обратно в инструмент.",
            "Выражения — только CSharpValue<T> и CSharpReference<T> (в JSON AST — строка C#-кода). " +
            "Лямбды (x => …) в аргументах запрещены: они не переживают сериализацию и ломают конструктор.",
            "Только активности и типы из палитры студии: whitelist отдаёт xaml.create_workflow_from_json " +
            "с {\"describe\": true}. Своих активностей, «похожих» имён и произвольных типов не бывает.",
            "Входные данные процесса — переменные (JSON: variables[], код: Sequence.Variables); " +
            "переменная объявляется до использования, тип указывается явно.",
            "Имена аргументов — только из палитры (аргумент проверяется по метаданным активности до вставки " +
            "в конструктор): придуманное имя возвращается ошибкой, а не «молча пустым» свойством.",
            "Один процесс — один файл .xaml в проекте (RPA-N\\Имя.xaml). Класс процесса, его пространство имён " +
            "и сборку проекта инструмент выводит сам, переопределять их нужно только при явной необходимости.",
            "Persistence и закладки (Bookmark) строятся только через Persist + Pick/Delay в Code-First: " +
            "блокировка, лямбда или своя активность в дерево не попадают.",
            "Конструктор — часть контракта: если после вызова файл не открылся и не прошёл валидацию, " +
            "вызов считается неуспешным, даже когда файл записан."
        };

        /// <summary>Выбор инструмента: агент не «придумывает» путь, а берёт подходящую строку матрицы.</summary>
        internal static readonly WorkflowAgentToolChoice[] ToolSelection =
        {
            new WorkflowAgentToolChoice(
                "1–5 простых шагов RPA, точечная правка уже открытого процесса",
                ContractToolNames.Palette + " (+ " + ContractToolNames.SetArgument + ")",
                "минимальный риск: активность и её аргументы проверяются по палитре до вставки в контейнер"),
            new WorkflowAgentToolChoice(
                "новый процесс из простых шагов: переменные, присваивания, вывод",
                ContractToolNames.Json,
                "детерминированно: одна строгая схема, один и тот же XAML на одном и том же описании"),
            new WorkflowAgentToolChoice(
                "ветвления и циклы без расчётов (If, ForEach<T>, While)",
                ContractToolNames.Json,
                "If/ForEach/While есть в whitelist JSON AST; результата проще достичь описанием, чем кодом"),
            new WorkflowAgentToolChoice(
                "вложенные ветвления и циклы с расчётами, InvokeMethod с параметрами",
                ContractToolNames.CodeFirst,
                "сложную логику короче выразить кодом; выражения всё равно остаются CSharpValue<T>/CSharpReference<T>"),
            new WorkflowAgentToolChoice(
                "TryCatch/Catch, Persist, Bookmark (ожидание внешнего события)",
                ContractToolNames.CodeFirst,
                "Catch и Persist не являются активностями палитры — их нельзя выразить JSON AST"),
            new WorkflowAgentToolChoice(
                "нужно понять, что уже есть в открытом процессе",
                ContractToolNames.GetWorkflow,
                "сначала снимок дерева, потом правка — иначе шаг вставляется не туда"),
            new WorkflowAgentToolChoice(
                "нужны правила, схема, whitelist или пример",
                ContractToolNames.Describe,
                "контракт и примеры отдаются машиночитаемо: не нужно угадывать формат и имена аргументов")
        };

        /// <summary>
        /// Правило самокоррекции: ошибка инструмента — это задача, а не отказ. Формулировки — шаги,
        /// чтобы агент не «сдавался» и не присылал XAML вместо исправленного входа.
        /// </summary>
        internal static readonly string[] SelfCorrection =
        {
            "ok:false — это задача, а не повод сдаться: прочитай errors[] (с путём до поля) и hints[].",
            "Исправляй вход (JSON-описание или код), а не XAML: поле xaml в ответе — диагностика, " +
            "его нельзя править и присылать назад.",
            "Ошибка аргумента — возьми имя и направление аргумента из подсказки палитры; " +
            "ошибка типа — объяви переменную с нужным типом; «нет активности» — сверься с whitelist.",
            "Ошибка «нет ссылки на сборку»/«неизвестный тип» лечится namespaces[]/references[] (JSON) " +
            "или using (код), а не заменой активности на «похожую».",
            "Повтори вызов того же инструмента с исправленным входом; лимит — 3 итерации на один процесс. " +
            "Если после третьей ошибка осталась, верни пользователю errors[] и hints[] и назови причину.",
            "Не понижай требования ради зелёного ответа: не заменяй выражение литералом, не выбрасывай ветку, " +
            "обработчик или Persist."
        };

        /// <summary>
        /// Few-shot примеры. Каждый проверен тестом-эталоном: прогон через конвейер генерации даёт
        /// канонический XAML, гейт валидации зелёный, файл повторно загружается, конструктор рисует дерево.
        /// </summary>
        internal static readonly WorkflowAgentExample[] Examples =
        {
            new WorkflowAgentExample(
                "sequence-assign",
                "Sequence + Assign",
                WorkflowAgentExampleFormat.Json,
                @"{
  ""variables"": [ { ""name"": ""output"", ""type"": ""String"" } ],
  ""activities"": [
    { ""type"": ""Assign"", ""displayName"": ""Присваивание"",
      ""arguments"": { ""To"": ""output"", ""Value"": { ""value"": ""готово"", ""type"": ""String"" } } },
    { ""type"": ""WriteLine"", ""displayName"": ""Вывод"",
      ""arguments"": { ""Text"": ""output"" } }
  ]
}",
                "Assign.To — ссылка на переменную (CSharpReference), Assign.Value — литерал через {\"value\": …}: " +
                "тип выводится из объявленной переменной, x:Object в XAML не попадает."),

            new WorkflowAgentExample(
                "if",
                "If",
                WorkflowAgentExampleFormat.Json,
                @"{
  ""variables"": [ { ""name"": ""counter"", ""type"": ""Int32"", ""default"": ""0"" } ],
  ""activities"": [
    { ""type"": ""If"", ""displayName"": ""Проверка"",
      ""arguments"": { ""Condition"": ""counter > 0"" },
      ""children"": {
        ""Then"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": { ""value"": ""не пусто"" } } } ],
        ""Else"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": { ""value"": ""пусто"" } } } ]
      } }
  ]
}",
                "Condition — строка C#-выражения; ветки Then/Else — массивы активностей, " +
                "пустая ветка допустима, но лучше её не оставлять."),

            new WorkflowAgentExample(
                "foreach",
                "ForEach<T>",
                WorkflowAgentExampleFormat.Json,
                @"{
  ""variables"": [ { ""name"": ""items"", ""type"": ""String[]"", ""default"": ""new string[0]"" } ],
  ""activities"": [
    { ""type"": ""ForEach"", ""typeArguments"": [ ""String"" ], ""displayName"": ""Обход"",
      ""arguments"": { ""Values"": { ""expression"": ""items"", ""type"": ""String[]"" } },
      ""children"": {
        ""Body"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""item"" } } ]
      } }
  ]
}",
                "Обобщённая активность требует typeArguments; тело — ActivityAction<T>, " +
                "элемент цикла доступен по имени item."),

            new WorkflowAgentExample(
                "while",
                "While",
                WorkflowAgentExampleFormat.Json,
                @"{
  ""variables"": [ { ""name"": ""counter"", ""type"": ""Int32"", ""default"": ""0"" } ],
  ""activities"": [
    { ""type"": ""While"", ""displayName"": ""Пока счётчик меньше 3"",
      ""arguments"": { ""Condition"": ""counter < 3"" },
      ""children"": {
        ""Body"": [
          { ""type"": ""Sequence"", ""displayName"": ""Тело цикла"",
            ""children"": {
              ""Activities"": [
                { ""type"": ""Assign"", ""displayName"": ""Прибавить"",
                  ""arguments"": { ""To"": ""counter"", ""Value"": ""counter + 1"" } },
                { ""type"": ""WriteLine"", ""displayName"": ""Значение"",
                  ""arguments"": { ""Text"": ""counter.ToString()"" } }
              ]
            } }
        ]
      } }
  ]
}",
                "While.Condition — свойство-выражение (Activity<bool>), поэтому условие задаётся строкой кода; " +
                "контейнер Body принимает одну активность, поэтому шаги тела обёрнуты в Sequence."),

            new WorkflowAgentExample(
                "trycatch",
                "TryCatch",
                WorkflowAgentExampleFormat.CodeFirst,
                @"using System;
using System.Activities;
using System.Activities.Statements;
using Microsoft.CSharp.Activities;

public static class Workflow
{
    public static Activity Build()
    {
        return new TryCatch
        {
            DisplayName = ""Обработка ошибок"",
            Try = new Sequence
            {
                Activities =
                {
                    new WriteLine
                    {
                        DisplayName = ""Работа"",
                        Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = ""\""шаг выполнен\"""" })
                    }
                }
            },
            Catches =
            {
                new Catch<Exception>
                {
                    Action = new ActivityAction<Exception>
                    {
                        Argument = new DelegateInArgument<Exception>(""error""),
                        Handler = new WriteLine
                        {
                            DisplayName = ""Лог ошибки"",
                            Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = ""error.Message"" })
                        }
                    }
                }
            },
            Finally = new WriteLine
            {
                DisplayName = ""Завершение"",
                Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = ""\""готово\"""" })
            }
        };
    }
}",
                "Catch и TryCatch.Catches не являются активностями палитры: JSON AST их не выражает, " +
                "поэтому обработка ошибок — только Code-First. У Catch нет DisplayName (это не Activity); " +
                "вход процесса — public static Activity Build()."),

            new WorkflowAgentExample(
                "bookmark-persist",
                "Bookmark + Persist",
                WorkflowAgentExampleFormat.CodeFirst,
                @"using System;
using System.Activities;
using System.Activities.Statements;
using Microsoft.CSharp.Activities;

public static class Workflow
{
    public static Activity Build()
    {
        return new Sequence
        {
            DisplayName = ""Ожидание внешнего события"",
            Activities =
            {
                new WriteLine
                {
                    DisplayName = ""Старт"",
                    Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = ""\""ждём сигнал\"""" })
                },
                new Persist
                {
                    DisplayName = ""Сохранить состояние""
                },
                new Pick
                {
                    DisplayName = ""Ожидание"",
                    Branches =
                    {
                        new PickBranch
                        {
                            DisplayName = ""Возобновление по таймауту"",
                            Trigger = new Delay
                            {
                                Duration = new InArgument<TimeSpan>(
                                    new CSharpValue<TimeSpan> { ExpressionText = ""TimeSpan.FromSeconds(30)"" })
                            },
                            Action = new WriteLine
                            {
                                DisplayName = ""Продолжение"",
                                Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = ""\""продолжили после паузы\"""" })
                            }
                        }
                    }
                }
            }
        };
    }
}",
                "Persist фиксирует состояние, Pick+Delay даёт точку возобновления (закладку): Persist → " +
                "перезапуск хоста → Resume. Лямбды и свои активности здесь запрещены — они не сериализуются.")
        };

        /// <summary>Пример по имени (без учёта регистра) или <c>null</c>.</summary>
        internal static WorkflowAgentExample FindExample(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return Examples.FirstOrDefault(example =>
                string.Equals(example.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Текст контракта для системного промпта агента: правила, матрица выбора инструмента,
        /// примеры и самокоррекция в одном блоке.
        /// </summary>
        internal static string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine("# Контракт генерации рабочего процесса (" + Version + ")");
            text.AppendLine();
            text.AppendLine("Результат работы агента — валидный XAML, который сразу открывается в конструкторе студии.");
            text.AppendLine();
            text.AppendLine("## Правила");
            for (int index = 0; index < Rules.Length; index++)
            {
                text.AppendLine((index + 1) + ". " + Rules[index]);
            }

            text.AppendLine();
            text.AppendLine("## Выбор инструмента");
            foreach (WorkflowAgentToolChoice choice in ToolSelection)
            {
                text.AppendLine("- " + choice.Situation + " → " + choice.Tool + " (" + choice.Reason + ")");
            }

            text.AppendLine();
            text.AppendLine("## Примеры");
            foreach (WorkflowAgentExample example in Examples)
            {
                text.AppendLine();
                text.AppendLine("### " + example.Name + " — " + example.Scenario + " (" + example.ToolName + ")");
                text.AppendLine("```" + example.FormatName);
                text.AppendLine(example.Payload);
                text.AppendLine("```");
                text.AppendLine("Почему так: " + example.Notes);
            }

            text.AppendLine();
            text.AppendLine("## Самокоррекция");
            for (int index = 0; index < SelfCorrection.Length; index++)
            {
                text.AppendLine((index + 1) + ". " + SelfCorrection[index]);
            }

            return text.ToString().TrimEnd();
        }
    }
}
