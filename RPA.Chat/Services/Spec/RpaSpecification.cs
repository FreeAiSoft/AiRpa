using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace RPA.Chat.Services.Spec
{
    /// <summary>Статус задачи декомпозиции (<c>TZ-05</c>).</summary>
    public enum SpecTaskStatus
    {
        Planned,
        InProgress,
        Done,
        Rejected
    }

    /// <summary>Статус неоднозначности (<c>TZ-09</c>).</summary>
    public enum SpecAmbiguityStatus
    {
        Open,
        Resolved,
        Deferred
    }

    /// <summary>Шаг ТЗ (<c>TZ-03</c>): что сделать, что должно получиться, какие данные нужны, какие ветвления.</summary>
    public sealed class SpecStep
    {
        public string Id { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        /// <summary>Ожидаемый результат шага — по нему строятся критерии и тест-кейсы.</summary>
        public string Expected { get; set; } = string.Empty;

        /// <summary>Ссылки на <see cref="SpecData.Id"/>.</summary>
        public List<string> DataRefs { get; set; } = new List<string>();

        /// <summary>Ветвления: «если … то …» отдельными пунктами.</summary>
        public List<string> Branches { get; set; } = new List<string>();
    }

    /// <summary>Данные ТЗ: имя, тип, откуда берётся, пример.</summary>
    public sealed class SpecData
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public string Example { get; set; } = string.Empty;
    }

    /// <summary>Исключительная ситуация ТЗ: ситуация → реакция робота.</summary>
    public sealed class SpecException
    {
        public string Situation { get; set; } = string.Empty;

        public string Reaction { get; set; } = string.Empty;
    }

    /// <summary>Критерий приёмки: текст и проверяемый признак.</summary>
    public sealed class SpecCriterion
    {
        public string Id { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        /// <summary>Чем проверяется — без этого критерий нельзя превратить в тест-кейс.</summary>
        public string Check { get; set; } = string.Empty;
    }

    /// <summary>Откуда взята спецификация: версия и sha исходника.</summary>
    public sealed class SpecSourceRef
    {
        public int Version { get; set; }

        public string Sha256 { get; set; } = string.Empty;

        /// <summary>Что именно прочитано: файл, диапазон абзацев, раздел.</summary>
        public string Range { get; set; } = string.Empty;
    }

    /// <summary>Неоднозначность ТЗ (<c>TZ-09</c>): вопрос, варианты, ответ.</summary>
    public sealed class SpecAmbiguity
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>Пункт ТЗ, к которому относится вопрос (<c>S3</c>, <c>A1</c>); пусто — ко всему ТЗ.</summary>
        public string ItemId { get; set; } = string.Empty;

        public string Question { get; set; } = string.Empty;

        public List<string> Options { get; set; } = new List<string>();

        public string Answer { get; set; } = string.Empty;

        public string AnsweredBy { get; set; } = string.Empty;

        public string AnsweredAtUtc { get; set; } = string.Empty;

        public SpecAmbiguityStatus Status { get; set; } = SpecAmbiguityStatus.Open;
    }

    /// <summary>Шаг плана задачи (<c>TZ-05</c>): что вставить и куда, без самого XAML.</summary>
    public sealed class SpecTaskStep
    {
        /// <summary>Тип активности из каталога студии.</summary>
        public string ActivityType { get; set; } = string.Empty;

        /// <summary>Адрес родителя (<c>root</c>, <c>root/Activities[0]</c>).</summary>
        public string Parent { get; set; } = "root";

        /// <summary>Контейнер родителя (<c>Activities</c>, <c>Then</c>, <c>Body</c>); пусто — единственный подходящий.</summary>
        public string Container { get; set; } = string.Empty;

        /// <summary>Имя аргумента активности, который заполняется на этом шаге.</summary>
        public string ArgumentName { get; set; } = string.Empty;

        /// <summary>Значение или выражение аргумента.</summary>
        public string ArgumentValue { get; set; } = string.Empty;
    }

    /// <summary>Задача декомпозиции ТЗ (<c>TZ-05</c>).</summary>
    public sealed class SpecTask
    {
        public string Id { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        /// <summary>Пункты ТЗ, которые закрывает задача: <c>S1</c>, <c>A2</c>.</summary>
        public List<string> SpecRefs { get; set; } = new List<string>();

        public SpecTaskStatus Status { get; set; } = SpecTaskStatus.Planned;

        /// <summary>Подсказки активностей каталога.</summary>
        public List<string> ActivityHints { get; set; } = new List<string>();

        /// <summary>Ссылки на <see cref="SpecAmbiguity.Id"/>, мешающие задаче.</summary>
        public List<string> OpenQuestions { get; set; } = new List<string>();

        public List<SpecTaskStep> Steps { get; set; } = new List<SpecTaskStep>();

        /// <summary>Адреса элементов workflow, созданных для задачи (<c>TZ-07</c>).</summary>
        public List<string> ElementIds { get; set; } = new List<string>();
    }

    /// <summary>Тест-кейс, построенный из критерия приёмки (<c>TZ-08</c>).</summary>
    public sealed class SpecTestCase
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>Ссылка на <see cref="SpecCriterion.Id"/>.</summary>
        public string CriterionId { get; set; } = string.Empty;

        /// <summary>Входные аргументы робота.</summary>
        public Dictionary<string, string> Inputs { get; set; } = new Dictionary<string, string>();

        /// <summary>Ожидаемые выходные аргументы.</summary>
        public Dictionary<string, string> ExpectedOutputs { get; set; } = new Dictionary<string, string>();

        /// <summary>Негативный кейс: ожидается падение/отказ, а не успешный результат.</summary>
        public bool Negative { get; set; }

        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// Трассировка «пункт ТЗ ↔ элемент workflow» (<c>TZ-06</c>). Адрес элемента — тот же
    /// <c>root/Activities[0]</c>, что у <c>workflow_outline</c> и инструментов правки.
    /// </summary>
    public sealed class SpecLink
    {
        public string ItemId { get; set; } = string.Empty;

        public string ElementId { get; set; } = string.Empty;

        public string Note { get; set; } = string.Empty;

        public string CreatedAtUtc { get; set; } = string.Empty;
    }

    /// <summary>
    /// Модель спецификации ТЗ (`TZ-03`, `schemaVersion` = 1). В модели нет ни одной WF-сущности:
    /// она переживает смену дизайнера и читается без студии.
    /// </summary>
    public sealed class RpaSpecification
    {
        /// <summary>Текущая версия схемы. Расширения — новыми полями, не переименованием.</summary>
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>Слаг каталога <c>docs\spec\&lt;slug&gt;</c>.</summary>
        public string Slug { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Goal { get; set; } = string.Empty;

        public List<string> Preconditions { get; set; } = new List<string>();

        public List<SpecStep> Steps { get; set; } = new List<SpecStep>();

        public List<SpecData> Data { get; set; } = new List<SpecData>();

        public List<SpecException> Exceptions { get; set; } = new List<SpecException>();

        public List<SpecCriterion> AcceptanceCriteria { get; set; } = new List<SpecCriterion>();

        public SpecSourceRef SourceRef { get; set; } = new SpecSourceRef();

        public List<SpecAmbiguity> Ambiguities { get; set; } = new List<SpecAmbiguity>();

        public List<SpecTask> Tasks { get; set; } = new List<SpecTask>();

        public List<SpecTestCase> TestCases { get; set; } = new List<SpecTestCase>();

        public List<SpecLink> Links { get; set; } = new List<SpecLink>();

        /// <summary>Найти шаг по идентификатору.</summary>
        public SpecStep FindStep(string id)
        {
            return Steps?.FirstOrDefault(step => Same(step.Id, id));
        }

        /// <summary>Найти критерий по идентификатору.</summary>
        public SpecCriterion FindCriterion(string id)
        {
            return AcceptanceCriteria?.FirstOrDefault(item => Same(item.Id, id));
        }

        /// <summary>Найти неоднозначность по идентификатору.</summary>
        public SpecAmbiguity FindAmbiguity(string id)
        {
            return Ambiguities?.FirstOrDefault(item => Same(item.Id, id));
        }

        /// <summary>Адреса всех пунктов ТЗ (шаги + критерии) — для проверки ссылок задач и меток.</summary>
        public IReadOnlyList<string> AllItemIds()
        {
            var ids = new List<string>();
            if (Steps != null) ids.AddRange(Steps.Where(s => !string.IsNullOrWhiteSpace(s.Id)).Select(s => s.Id));
            if (AcceptanceCriteria != null)
            {
                ids.AddRange(AcceptanceCriteria.Where(c => !string.IsNullOrWhiteSpace(c.Id)).Select(c => c.Id));
            }

            return ids;
        }

        /// <summary>Пустая спецификация для каталога ТЗ.</summary>
        public static RpaSpecification CreateEmpty(string slug, string title = null)
        {
            return new RpaSpecification
            {
                Slug = slug ?? string.Empty,
                Title = string.IsNullOrWhiteSpace(title) ? (slug ?? string.Empty) : title
            };
        }

        private static bool Same(string left, string right)
        {
            return string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------
        // JSON
        // ------------------------------------------------------------------

        /// <summary>Настройки сериализации: camelCase, отступы, пустые значения не пишутся, enum — строкой.</summary>
        /// <summary>
        /// Резолвер, который не переименовывает ключи словарей: имена аргументов робота — идентификаторы C#,
        /// «InputText» не должен превращаться в «inputText» (иначе тест-кейсы не совпадают с аргументами).
        /// </summary>
        private static IContractResolver DictionarySafeResolver()
        {
            var resolver = new CamelCasePropertyNamesContractResolver();
            resolver.NamingStrategy.ProcessDictionaryKeys = false;
            resolver.NamingStrategy.OverrideSpecifiedNames = true;
            return resolver;
        }

        public static JsonSerializerSettings JsonSettings()
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Include,
                ContractResolver = DictionarySafeResolver()
            };
            settings.Converters.Add(new StringEnumConverter(new CamelCaseNamingStrategy()));
            return settings;
        }

        /// <summary>Сериализует спецификацию в JSON (`specification.json` — источник правды).</summary>
        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, JsonSettings());
        }

        /// <summary>Читает спецификацию из JSON. Бросает <see cref="JsonException"/> на битом файле.</summary>
        public static RpaSpecification FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new JsonException("пустой JSON спецификации");
            }

            var spec = JsonConvert.DeserializeObject<RpaSpecification>(json, JsonSettings());
            if (spec == null)
            {
                throw new JsonException("JSON спецификации не разобран");
            }

            spec.Normalize();
            return spec;
        }

        /// <summary>Читает спецификацию из файла; ошибки — текстом, без исключения.</summary>
        public static bool TryLoad(string path, out RpaSpecification specification, out string error)
        {
            specification = null;
            error = null;

            try
            {
                if (!File.Exists(path))
                {
                    error = "файл спецификации не найден: " + path;
                    return false;
                }

                specification = FromJson(File.ReadAllText(path));
                return true;
            }
            catch (Exception ex)
            {
                error = "спецификация не прочитана: " + ex.Message;
                return false;
            }
        }

        /// <summary>Приводит коллекции к непустым и подставляет версию схемы.</summary>
        public void Normalize()
        {
            SchemaVersion = SchemaVersion <= 0 ? CurrentSchemaVersion : SchemaVersion;
            Slug = Slug ?? string.Empty;
            Title = Title ?? string.Empty;
            Goal = Goal ?? string.Empty;
            Preconditions = Preconditions ?? new List<string>();
            Steps = Steps ?? new List<SpecStep>();
            Data = Data ?? new List<SpecData>();
            Exceptions = Exceptions ?? new List<SpecException>();
            AcceptanceCriteria = AcceptanceCriteria ?? new List<SpecCriterion>();
            SourceRef = SourceRef ?? new SpecSourceRef();
            Ambiguities = Ambiguities ?? new List<SpecAmbiguity>();
            Tasks = Tasks ?? new List<SpecTask>();
            TestCases = TestCases ?? new List<SpecTestCase>();
            Links = Links ?? new List<SpecLink>();

            foreach (var step in Steps)
            {
                step.Id = step.Id ?? string.Empty;
                step.Text = step.Text ?? string.Empty;
                step.Expected = step.Expected ?? string.Empty;
                step.DataRefs = step.DataRefs ?? new List<string>();
                step.Branches = step.Branches ?? new List<string>();
            }

            foreach (var data in Data)
            {
                data.Id = data.Id ?? string.Empty;
                data.Name = data.Name ?? string.Empty;
                data.Type = data.Type ?? string.Empty;
                data.Source = data.Source ?? string.Empty;
                data.Example = data.Example ?? string.Empty;
            }

            foreach (var item in Exceptions)
            {
                item.Situation = item.Situation ?? string.Empty;
                item.Reaction = item.Reaction ?? string.Empty;
            }

            foreach (var criterion in AcceptanceCriteria)
            {
                criterion.Id = criterion.Id ?? string.Empty;
                criterion.Text = criterion.Text ?? string.Empty;
                criterion.Check = criterion.Check ?? string.Empty;
            }

            foreach (var ambiguity in Ambiguities)
            {
                ambiguity.Id = ambiguity.Id ?? string.Empty;
                ambiguity.ItemId = ambiguity.ItemId ?? string.Empty;
                ambiguity.Question = ambiguity.Question ?? string.Empty;
                ambiguity.Options = ambiguity.Options ?? new List<string>();
                ambiguity.Answer = ambiguity.Answer ?? string.Empty;
                ambiguity.AnsweredBy = ambiguity.AnsweredBy ?? string.Empty;
                ambiguity.AnsweredAtUtc = ambiguity.AnsweredAtUtc ?? string.Empty;
            }

            foreach (var task in Tasks)
            {
                task.Id = task.Id ?? string.Empty;
                task.Title = task.Title ?? string.Empty;
                task.SpecRefs = task.SpecRefs ?? new List<string>();
                task.ActivityHints = task.ActivityHints ?? new List<string>();
                task.OpenQuestions = task.OpenQuestions ?? new List<string>();
                task.ElementIds = task.ElementIds ?? new List<string>();
                task.Steps = task.Steps ?? new List<SpecTaskStep>();
                foreach (var taskStep in task.Steps)
                {
                    taskStep.ActivityType = taskStep.ActivityType ?? string.Empty;
                    taskStep.Parent = string.IsNullOrWhiteSpace(taskStep.Parent) ? "root" : taskStep.Parent;
                    taskStep.Container = taskStep.Container ?? string.Empty;
                    taskStep.ArgumentName = taskStep.ArgumentName ?? string.Empty;
                    taskStep.ArgumentValue = taskStep.ArgumentValue ?? string.Empty;
                }
            }

            foreach (var testCase in TestCases)
            {
                testCase.Id = testCase.Id ?? string.Empty;
                testCase.CriterionId = testCase.CriterionId ?? string.Empty;
                testCase.Inputs = testCase.Inputs ?? new Dictionary<string, string>();
                testCase.ExpectedOutputs = testCase.ExpectedOutputs ?? new Dictionary<string, string>();
                testCase.Note = testCase.Note ?? string.Empty;
            }

            foreach (var link in Links)
            {
                link.ItemId = link.ItemId ?? string.Empty;
                link.ElementId = link.ElementId ?? string.Empty;
                link.Note = link.Note ?? string.Empty;
                link.CreatedAtUtc = link.CreatedAtUtc ?? string.Empty;
            }
        }
    }
}
