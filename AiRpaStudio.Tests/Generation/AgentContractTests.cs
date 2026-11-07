using System;
using System.Activities;
using System.Activities.XamlIntegration;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 7 конвейера генерации XAML: контракт для агента
/// (<see cref="WorkflowAgentContract"/>).
/// <para>
/// Контракт — не «текст в документе», а исполняемое обещание: каждый few-shot пример проверен здесь
/// прогоном через настоящий конвейер (описание/код → объектная модель WF → канонический XAML → гейт
/// валидации → повторная загрузка файла). Поэтому пример из документации не может «не собираться»,
/// а правила (только <c>CSharpValue&lt;T&gt;</c>/<c>CSharpReference&lt;T&gt;</c>, только whitelist
/// палитры, самокоррекция вместо отказа) зафиксированы тестами.
/// </para>
/// </summary>
[TestFixture]
public class AgentContractTests
{
    /// <summary>Сценарии, которые контракт обязан показать агенту (п. 3 этапа 7 плана).</summary>
    private static readonly string[] RequiredExamples =
    {
        "sequence-assign",
        "if",
        "foreach",
        "while",
        "trycatch",
        "bookmark-persist"
    };

    private readonly List<string> _temporaryDirectories = new List<string>();

    [TearDown]
    public void CleanupTemporaryDirectories()
    {
        foreach (string directory in _temporaryDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch (Exception)
            {
                // Каталог мог остаться заблокированным — на результат теста это не влияет.
            }
        }

        _temporaryDirectories.Clear();
    }

    // --- Состав контракта. ---

    [Test]
    public void Contract_CoversRequiredExamples()
    {
        string[] names = WorkflowAgentContract.Examples.Select(example => example.Name).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(names, Is.EquivalentTo(RequiredExamples),
                "контракт обязан содержать все обязательные сценарии этапа 7");
            Assert.That(names.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(names.Length),
                "имена примеров уникальны: по ним инструмент отдаёт один пример");

            foreach (WorkflowAgentExample example in WorkflowAgentContract.Examples)
            {
                Assert.That(example.Scenario, Is.Not.Null.And.Not.Empty, example.Name + ": сценарий");
                Assert.That(example.Payload, Is.Not.Null.And.Not.Empty, example.Name + ": тело примера");
                Assert.That(example.Notes, Is.Not.Null.And.Not.Empty, example.Name + ": пояснение «почему так»");
                Assert.That(example.ToolName, Is.EqualTo(example.Format == WorkflowAgentExampleFormat.Json
                        ? ContractToolNames.Json
                        : ContractToolNames.CodeFirst),
                    example.Name + ": инструмент соответствует форме примера");
            }
        });
    }

    [Test]
    public void Rules_StateHardConstraints()
    {
        string rules = string.Join("\n", WorkflowAgentContract.Rules);

        Assert.Multiple(() =>
        {
            Assert.That(WorkflowAgentContract.Rules.Length, Is.GreaterThanOrEqualTo(6));

            // п. 1.1–1.2 плана: выражения только CSharpValue/CSharpReference, лямбды запрещены.
            Assert.That(rules, Does.Contain("CSharpValue<T>"));
            Assert.That(rules, Does.Contain("CSharpReference<T>"));
            Assert.That(rules, Does.Contain("Лямбды"));

            // XAML — результат сериализации, а не текст агента.
            Assert.That(rules, Does.Contain("не пиши XAML текстом"));

            // Только палитра студии и её типы.
            Assert.That(rules, Does.Contain("палитры студии"));
            Assert.That(rules, Does.Contain("whitelist"));

            // Конструктор — часть контракта (п. 1.3 плана).
            Assert.That(rules, Does.Contain("открывается в конструкторе"));
        });
    }

    [Test]
    public void ToolSelection_CoversAllEntryPoints()
    {
        string tools = string.Join("\n", WorkflowAgentContract.ToolSelection.Select(choice => choice.Tool));

        Assert.Multiple(() =>
        {
            Assert.That(tools, Does.Contain(ContractToolNames.Palette), "простое → палитра");
            Assert.That(tools, Does.Contain(ContractToolNames.CodeFirst), "сложная логика → Code-First");
            Assert.That(tools, Does.Contain(ContractToolNames.Json), "детерминизм → JSON AST");
            Assert.That(tools, Does.Contain(ContractToolNames.GetWorkflow), "сначала снимок дерева");
            Assert.That(tools, Does.Contain(ContractToolNames.Describe), "правила и whitelist — из студии");

            foreach (WorkflowAgentToolChoice choice in WorkflowAgentContract.ToolSelection)
            {
                Assert.That(choice.Situation, Is.Not.Null.And.Not.Empty);
                Assert.That(choice.Reason, Is.Not.Null.And.Not.Empty, choice.Situation + ": причина выбора");
            }
        });
    }

    [Test]
    public void SelfCorrection_TreatsGateErrorAsTask()
    {
        string steps = string.Join("\n", WorkflowAgentContract.SelfCorrection);

        Assert.Multiple(() =>
        {
            Assert.That(steps, Does.Contain("это задача"),
                "ошибка гейта — задача для агента, а не повод сдаться");
            Assert.That(steps, Does.Contain("errors[]"), "агент читает структурированные ошибки");
            Assert.That(steps, Does.Contain("hints[]"), "и подсказки, а не только текст ошибки");
            Assert.That(steps, Does.Contain("а не XAML"), "XAML — диагностика, а не вход для правки");
            Assert.That(steps, Does.Contain("3 итерации"), "лимит попыток назван явно");
        });
    }

    // --- Примеры проверены настоящим конвейером. ---

    [Test]
    public void JsonExamples_BuildCanonicalXamlAndPassGate()
    {
        foreach (WorkflowAgentExample example in JsonExamples())
        {
            string path = NewWorkflowPath(example.Name);

            JsonWorkflowResult result = new JsonWorkflowService().Create(new JsonWorkflowRequest
            {
                Json = example.Payload,
                FilePath = path,
                ProjectName = WorkflowTestSamples.ProjectName,
                NamespaceName = WorkflowTestSamples.WorkflowNamespace,
                ClassName = WorkflowTestSamples.WorkflowClass
            });

            Assert.That(result.Ok, Is.True,
                "пример «" + example.Name + "» не прошёл конвейер." + Environment.NewLine + result.Describe());

            Assert.That(File.Exists(path), Is.True, example.Name + ": файл процесса записан");
            AssertCanonicalXaml(example, result.Xaml);

            // Канонический XAML обязан повторно загружаться как WF-активность (п. 1.3 плана).
            Activity reloaded = ActivityXamlServices.Load(path);
            Assert.That(reloaded, Is.Not.Null, example.Name + ": XAML не загружается повторно");
        }
    }

    [Test]
    public void CodeFirstExamples_BuildCanonicalXamlAndPassGate()
    {
        foreach (WorkflowAgentExample example in CodeFirstExamples())
        {
            string path = NewWorkflowPath(example.Name);

            CodeFirstResult result = new CodeFirstWorkflowService().Create(new CodeFirstWorkflowRequest
            {
                Code = example.Payload,
                FilePath = path,
                ProjectName = WorkflowTestSamples.ProjectName,
                NamespaceName = WorkflowTestSamples.WorkflowNamespace,
                ClassName = WorkflowTestSamples.WorkflowClass
            });

            Assert.That(result.Ok, Is.True,
                "пример «" + example.Name + "» не прошёл конвейер." + Environment.NewLine + result.Describe());

            Assert.That(File.Exists(path), Is.True, example.Name + ": файл процесса записан");
            AssertCanonicalXaml(example, result.Xaml);

            Activity reloaded = ActivityXamlServices.Load(path);
            Assert.That(reloaded, Is.Not.Null, example.Name + ": XAML не загружается повторно");
        }
    }

    [Test]
    public void JsonExamples_UseOnlyWhitelistedActivitiesAndTypes()
    {
        IReadOnlyList<string> activities = WorkflowJsonSchema.ActivityKeys();
        IReadOnlyList<string> types = WorkflowJsonSchema.TypeNames();

        foreach (WorkflowAgentExample example in JsonExamples())
        {
            JObject document = JObject.Parse(example.Payload);

            AssertVariablesWhitelisted(document, example, types);

            foreach (JObject activity in DocumentActivities(document))
            {
                string type = activity["type"]?.Value<string>();
                Assert.That(activities, Does.Contain(type),
                    example.Name + ": активность «" + type + "» вне whitelist палитры — пример учил бы агента ошибке");

                if (activity["typeArguments"] is JArray typeArguments)
                {
                    foreach (JToken argument in typeArguments)
                    {
                        Assert.That(types, Does.Contain(argument.Value<string>()),
                            example.Name + ": тип «" + argument.Value<string>() + "» вне whitelist");
                    }
                }
            }
        }
    }

    [Test]
    public void Examples_ContainNoLambdas()
    {
        foreach (WorkflowAgentExample example in WorkflowAgentContract.Examples)
        {
            Assert.Multiple(() =>
            {
                Assert.That(example.Payload, Does.Not.Contain("=>"),
                    example.Name + ": лямбда в примере — она не переживает сериализацию");
                Assert.That(example.Payload, Does.Not.Contain("LambdaValue"));
                Assert.That(example.Payload, Does.Not.Contain("LambdaReference"));
            });
        }
    }

    // --- Текст для промпта и документация. ---

    [Test]
    public void Describe_ReturnsPromptTextWithRulesAndExamples()
    {
        string text = WorkflowAgentContract.Describe();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain(WorkflowAgentContract.Version), "версия контракта видна агенту");
            Assert.That(text, Does.Contain("## Правила"));
            Assert.That(text, Does.Contain("## Выбор инструмента"));
            Assert.That(text, Does.Contain("## Примеры"));
            Assert.That(text, Does.Contain("## Самокоррекция"));

            foreach (string rule in WorkflowAgentContract.Rules)
            {
                Assert.That(text, Does.Contain(rule), "правило попало в текст промпта целиком");
            }

            foreach (WorkflowAgentExample example in WorkflowAgentContract.Examples)
            {
                Assert.That(text, Does.Contain(example.Name), example.Name + ": имя примера в тексте");
                Assert.That(Normalize(text), Does.Contain(Normalize(example.Payload)),
                    example.Name + ": тело примера в тексте промпта");
            }
        });
    }

    [Test]
    public void Doc_MirrorsContract()
    {
        string doc = Normalize(ReadSource(Path.Combine("AiRpaStudio", "docs", "Workflow_Agent_Contract.md")));
        string docWithoutBackticks = doc.Replace("`", string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain(ContractToolNames.Describe), "документ объясняет, откуда берётся контракт");

            foreach (string rule in WorkflowAgentContract.Rules)
            {
                Assert.That(doc, Does.Contain(Normalize(rule)),
                    "правило из кода отсутствует в docs\\Workflow_Agent_Contract.md — документация разошлась с контрактом");
            }

            foreach (WorkflowAgentToolChoice choice in WorkflowAgentContract.ToolSelection)
            {
                Assert.That(doc, Does.Contain(Normalize(choice.Situation)), "ситуация выбора инструмента в документе");
                Assert.That(docWithoutBackticks, Does.Contain(choice.Tool),
                    "инструмент «" + choice.Tool + "» упомянут в документе");
            }

            foreach (WorkflowAgentExample example in WorkflowAgentContract.Examples)
            {
                Assert.That(doc, Does.Contain("### " + example.Name + " — " + example.Scenario),
                    example.Name + ": раздел примера с тем же сценарием");
                Assert.That(doc, Does.Contain(Normalize(example.Payload)),
                    example.Name + ": тело примера в документе обязано совпадать с проверенным в тестах");
                Assert.That(doc, Does.Contain(Normalize(example.Notes)), example.Name + ": пояснение примера");
            }

            foreach (string step in WorkflowAgentContract.SelfCorrection)
            {
                Assert.That(doc, Does.Contain(Normalize(step)), "правило самокоррекции в документе");
            }
        });
    }

    // --- Вспомогательное. ---

    private static IEnumerable<WorkflowAgentExample> JsonExamples()
    {
        return WorkflowAgentContract.Examples.Where(example => example.Format == WorkflowAgentExampleFormat.Json);
    }

    private static IEnumerable<WorkflowAgentExample> CodeFirstExamples()
    {
        return WorkflowAgentContract.Examples.Where(example => example.Format == WorkflowAgentExampleFormat.CodeFirst);
    }

    private static void AssertCanonicalXaml(WorkflowAgentExample example, string xaml)
    {
        Assert.That(xaml, Is.Not.Null.And.Not.Empty, example.Name + ": XAML собран");

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("x:Class=\"" + WorkflowTestSamples.WorkflowNamespace + "." +
                                           WorkflowTestSamples.WorkflowClass + "\""),
                example.Name + ": x:Class процесса");
            Assert.That(xaml, Does.Contain("sap2010:ExpressionActivityEditor.ExpressionActivityEditor=\"C#\""),
                example.Name + ": без редактора C# выражения в конструкторе не редактируются");
            Assert.That(xaml, Does.Contain("xmlns:mca=\""), example.Name + ": пространство имён C#-выражений");
            Assert.That(xaml, Does.Contain("TextExpression.ReferencesForImplementation"),
                example.Name + ": ссылки на сборки для выражений");
            Assert.That(xaml, Does.Not.Contain("LambdaValue"), example.Name + ": лямбд в XAML быть не может");
            Assert.That(xaml, Does.Not.Contain("LambdaReference"), example.Name + ": лямбд в XAML быть не может");
        });
    }

    private static void AssertVariablesWhitelisted(JObject document, WorkflowAgentExample example, IReadOnlyList<string> types)
    {
        if (!(document["variables"] is JArray variables))
        {
            return;
        }

        foreach (JObject variable in variables.OfType<JObject>())
        {
            string type = variable["type"]?.Value<string>();
            Assert.That(types, Does.Contain(type),
                example.Name + ": тип переменной «" + type + "» вне whitelist");
        }
    }

    /// <summary>Все активности документа: корень/список шагов и любые вложенные контейнеры.</summary>
    private static IEnumerable<JObject> DocumentActivities(JObject document)
    {
        var result = new List<JObject>();

        foreach (JToken token in document["activities"] as JArray ?? new JArray())
        {
            Collect(token, result);
        }

        Collect(document["root"], result);
        return result;
    }

    private static void Collect(JToken token, List<JObject> result)
    {
        if (token is JArray array)
        {
            foreach (JToken item in array)
            {
                Collect(item, result);
            }

            return;
        }

        if (!(token is JObject activity))
        {
            return;
        }

        if (activity["type"] != null)
        {
            result.Add(activity);
        }

        if (activity["children"] is JObject children)
        {
            foreach (JProperty container in children.Properties())
            {
                Collect(container.Value, result);
            }
        }
    }

    private string NewWorkflowPath(string exampleName)
    {
        string root = WorkflowTestSamples.CreateTempDirectory();
        _temporaryDirectories.Add(root);

        string directory = Path.Combine(root, WorkflowTestSamples.ProjectName);
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, exampleName + ".xaml");
    }

    /// <summary>Сравнение текста без зависимости от переводов строк и хвостовых пробелов.</summary>
    private static string Normalize(string text)
    {
        return string.Join(
            "\n",
            (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(line => line.TrimEnd()));
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, "не найден файл " + relativePath);
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }
}
