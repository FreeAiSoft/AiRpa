using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RPA.Core.Generation
{
    /// <summary>
    /// JSON-схема процесса (этап 6 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <para>
    /// Схема намеренно строгая и полностью описана здесь, в одном месте: валидатор
    /// (<see cref="WorkflowJsonValidator"/>) проверяет по этим спискам полей и whitelist'ам, а агент
    /// получает тот же перечень в ответе инструмента <c>xaml.create_workflow_from_json</c>
    /// (<c>describe: true</c>) — расхождение «в схеме есть, а валидатор не знает» невозможно.
    /// </para>
    /// <para>
    /// Принципы:
    /// </para>
    /// <list type="bullet">
    /// <item>неизвестное поле — ошибка (опечатка в имени поля ловится до построения модели);</item>
    /// <item>неизвестная активность — ошибка: whitelist строится по палитре студии
    /// (<see cref="ActivityPaletteRegistry"/>), а не по «примерному» списку;</item>
    /// <item>тип переменной/аргумента — только из whitelist <see cref="PaletteTypeNames"/>;</item>
    /// <item>строка в аргументе — это <b>C#-выражение</b> (п. 1.1 плана: выражения хранятся как строки кода);
    /// литерал задаётся явно: <c>{"value": "готово"}</c>.</item>
    /// </list>
    /// </summary>
    internal static class WorkflowJsonSchema
    {
        /// <summary>Предел размера описания: защита от «случайно вставленного» файла целиком.</summary>
        internal const int MaxJsonChars = 200 * 1024;

        /// <summary>Предел вложенности активностей: защита от цикла в описании.</summary>
        internal const int MaxDepth = 32;

        /// <summary>Предел числа активностей в описании (диаграмма больше нечитаема и не нужна).</summary>
        internal const int MaxActivities = 500;

        /// <summary>Поля описания процесса.</summary>
        internal static readonly string[] DocumentFields =
        {
            "name",
            "className",
            "namespace",
            "project",
            "description",
            "schemaVersion",
            "$schema",
            "variables",
            "namespaces",
            "references",
            "activities",
            "root",
            "activity"
        };

        /// <summary>Поля активности.</summary>
        internal static readonly string[] ActivityFields =
        {
            "type",
            "activityType",
            "displayName",
            "typeArguments",
            "arguments",
            "children",
            "variables"
        };

        /// <summary>Поля переменной.</summary>
        internal static readonly string[] VariableFields = { "name", "type", "typeName", "default" };

        /// <summary>Поля значения аргумента в объектной форме.</summary>
        internal static readonly string[] ValueFields = { "expression", "expr", "value", "type", "typeName" };

        /// <summary>Имена активностей, разрешённых в JSON (whitelist палитры студии).</summary>
        internal static IReadOnlyList<string> ActivityKeys(ActivityPaletteRegistry registry = null)
        {
            ActivityPaletteRegistry source = registry ?? ActivityPaletteRegistry.Default;
            return source.All.Select(descriptor => descriptor.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray();
        }

        /// <summary>Имена типов, разрешённых для переменных и аргументов типа.</summary>
        internal static IReadOnlyList<string> TypeNames() => PaletteTypeNames.KnownNamesList;

        /// <summary>Человеческое описание схемы — для подсказок в ответе инструмента.</summary>
        internal static string Describe()
        {
            return "описание процесса: { project?, name?, namespace?, variables[], namespaces[], references[], " +
                   "activities[] | root } ; активность: { type, displayName?, typeArguments?, arguments{}, children{}, variables? } ; " +
                   "переменная: { name, type, default? } ; значение аргумента: \"C#-выражение\" | {\"expression\": \"...\", \"type\": \"String\"} | " +
                   "{\"value\": <литерал>}";
        }

        /// <summary>Минимальный рабочий пример: по нему агент собирает первый вариант без догадок.</summary>
        internal static string Example()
        {
            return @"{
  ""project"": ""RPA-1"",
  ""variables"": [ { ""name"": ""output"", ""type"": ""String"" } ],
  ""activities"": [
    { ""type"": ""Assign"", ""displayName"": ""Присваивание"",
      ""arguments"": { ""To"": ""output"", ""Value"": ""\""Hello from AiRpaStudio\"""" } },
    { ""type"": ""WriteLine"", ""displayName"": ""Вывод"",
      ""arguments"": { ""Text"": ""output"" } }
  ]
}";
        }

        /// <summary>
        /// Машиночитаемая схема (JSON Schema draft-07) для агента: те же поля и whitelist'ы, что проверяет
        /// валидатор. Отдаётся инструментом по <c>describe: true</c>.
        /// </summary>
        internal static JObject ToJsonSchema(ActivityPaletteRegistry registry = null)
        {
            return new JObject
            {
                ["$schema"] = "http://json-schema.org/draft-07/schema#",
                ["title"] = "Рабочий процесс AiRpaStudio (JSON AST)",
                ["description"] =
                    "Строгая схема: неизвестные поля, активности и типы отклоняются до построения модели WF. " +
                    "Строка в аргументе — C#-выражение, литерал — только через {value: ...}.",
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["name"] = StringField("Имя класса процесса (x:Class); по умолчанию — имя файла"),
                    ["namespace"] = StringField("Пространство имён класса процесса"),
                    ["project"] = StringField("Имя сборки проекта для TextExpression.ReferencesForImplementation"),
                    ["description"] = StringField("Комментарий к процессу"),
                    ["schemaVersion"] = StringField("Версия схемы (справочно)"),
                    ["variables"] = ArrayOf(VariableSchema(), "Переменные процесса (попадут в корневой Sequence)"),
                    ["namespaces"] = ArrayOf(StringField("Пространство имён C#-выражений"), "Дополнительные using для выражений"),
                    ["references"] = ArrayOf(StringField("Простое имя сборки"), "Дополнительные ссылки на сборки"),
                    ["activities"] = new JObject
                    {
                        ["type"] = "array",
                        ["minItems"] = 1,
                        ["description"] = "Шаги процесса: корнем станет Sequence",
                        ["items"] = ActivitySchemaRef()
                    },
                    ["root"] = new JObject
                    {
                        ["description"] = "Явный корень процесса (альтернатива activities)",
                        ["$ref"] = "#/definitions/activity"
                    }
                },
                ["definitions"] = new JObject
                {
                    ["activity"] = ActivitySchema(registry),
                    ["variable"] = VariableSchema()
                }
            };
        }

        private static JObject ActivitySchema(ActivityPaletteRegistry registry)
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JArray("type"),
                ["properties"] = new JObject
                {
                    ["type"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Имя активности палитры; допустимые значения — в enum",
                        ["enum"] = new JArray(ActivityKeys(registry).Cast<object>().ToArray())
                    },
                    ["displayName"] = StringField("Подпись активности на диаграмме"),
                    ["typeArguments"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Аргументы типа обобщённых активностей (ForEach<T> и т. п.)",
                        ["items"] = new JObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JArray(TypeNames().Cast<object>().ToArray())
                        }
                    },
                    ["arguments"] = new JObject
                    {
                        ["type"] = "object",
                        ["description"] = "Значения аргументов: имя аргумента палитры → значение",
                        ["additionalProperties"] = ValueSchema()
                    },
                    ["children"] = new JObject
                    {
                        ["type"] = "object",
                        ["description"] = "Вложенные активности по имени контейнера (Activities, Then, Body…)",
                        ["additionalProperties"] = new JObject
                        {
                            ["type"] = "array",
                            ["items"] = ActivitySchemaRef()
                        }
                    },
                    ["variables"] = ArrayOf(VariableSchema(), "Переменные контейнера (Sequence.Variables)")
                }
            };
        }

        private static JObject VariableSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JArray("name", "type"),
                ["properties"] = new JObject
                {
                    ["name"] = StringField("Имя переменной (идентификатор C#)"),
                    ["type"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Тип переменной из whitelist",
                        ["enum"] = new JArray(TypeNames().Cast<object>().ToArray())
                    },
                    ["default"] = new JObject
                    {
                        ["description"] = "Значение по умолчанию: C#-выражение или {\"value\": ...}"
                    }
                }
            };
        }

        private static JObject ValueSchema()
        {
            return new JObject
            {
                ["description"] =
                    "C#-выражение (строка), литерал или объект {\"expression\"|\"value\", \"type\"}; " +
                    "лямбды запрещены — только CSharpValue<T>/CSharpReference<T>",
                ["oneOf"] = new JArray
                {
                    new JObject { ["type"] = "string" },
                    new JObject { ["type"] = "number" },
                    new JObject { ["type"] = "boolean" },
                    new JObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new JObject
                        {
                            ["expression"] = StringField("C#-выражение"),
                            ["value"] = new JObject { ["description"] = "Литеральное значение" },
                            ["type"] = new JObject
                            {
                                ["type"] = "string",
                                ["enum"] = new JArray(TypeNames().Cast<object>().ToArray())
                            }
                        }
                    }
                }
            };
        }

        private static JObject ActivitySchemaRef()
        {
            return new JObject { ["$ref"] = "#/definitions/activity" };
        }

        private static JObject StringField(string description)
        {
            return new JObject { ["type"] = "string", ["description"] = description };
        }

        private static JObject ArrayOf(JObject items, string description)
        {
            return new JObject
            {
                ["type"] = "array",
                ["description"] = description,
                ["items"] = items
            };
        }
    }
}
