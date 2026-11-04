using System;
using System.Activities;
using System.Activities.XamlIntegration;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 6 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>: JSON AST → объектная модель WF →
/// общий конвейер этапов 2–3 (каноника, сериализатор WF, гейт, запись файла).
/// <para>
/// Проверяется, что описание процесса даёт тот же канонический XAML, что и палитра/Code-First:
/// <c>x:Class</c>, редактор выражений <c>C#</c>, <c>xmlns:mca</c>, переменные и выражения как
/// <c>CSharpValue&lt;T&gt;</c>/<c>CSharpReference&lt;T&gt;</c> — без «сырых» XAML-строк от агента.
/// Отдельно — негативные сценарии: при ошибке файл не появляется в проекте.
/// </para>
/// </summary>
[TestFixture]
public class JsonToWorkflowTests
{
    private const string SimpleDocument = @"{
  ""variables"": [ { ""name"": ""output"", ""type"": ""String"" } ],
  ""activities"": [
    { ""type"": ""Assign"", ""displayName"": ""Присваивание"",
      ""arguments"": { ""To"": ""output"", ""Value"": ""\""Hello from JSON\"""" } },
    { ""type"": ""WriteLine"", ""displayName"": ""Вывод"",
      ""arguments"": { ""Text"": ""output"" } }
  ]
}";

    private const string NestedDocument = @"{
  ""variables"": [ { ""name"": ""counter"", ""type"": ""Int32"", ""default"": ""0"" } ],
  ""activities"": [
    { ""type"": ""If"", ""displayName"": ""Если"",
      ""arguments"": { ""Condition"": ""counter == 0"" },
      ""children"": {
        ""Then"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""\""тогда\"""" } } ],
        ""Else"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""\""иначе\"""" } } ]
      } },
    { ""type"": ""ForEach"", ""typeArguments"": [ ""String"" ], ""displayName"": ""Цикл"",
      ""arguments"": { ""Values"": { ""expression"": ""new string[0]"", ""type"": ""String[]"" } },
      ""children"": {
        ""Body"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""\""в цикле\"""" } } ]
      } }
  ]
}";

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

    [Test]
    public void SimpleDocument_BuildsCanonicalXaml()
    {
        JsonWorkflowResult result = Build(SimpleDocument);

        Assert.That(result.Ok, Is.True, result.Describe());

        Assert.Multiple(() =>
        {
            Assert.That(result.Xaml, Does.Contain("x:Class=\"RPA_1.Main\""));
            Assert.That(result.Xaml, Does.Contain("sap2010:ExpressionActivityEditor.ExpressionActivityEditor=\"C#\""),
                "без редактора C# выражения в конструкторе не редактируются");
            Assert.That(result.Xaml, Does.Contain("xmlns:mca=\""),
                "пространство имён C#-выражений обязательно");
            Assert.That(result.Xaml, Does.Contain("TextExpression.ReferencesForImplementation"));
            Assert.That(result.Xaml, Does.Contain("<Variable x:TypeArguments=\"x:String\" Name=\"output\""),
                "переменные из описания попали в Sequence.Variables");
            Assert.That(result.Xaml, Does.Contain("mca:CSharpReference"), "To — ссылка CSharpReference<T>");
            Assert.That(result.Xaml, Does.Contain("mca:CSharpValue"), "значения — CSharpValue<T>");
            Assert.That(result.Xaml, Does.Contain("<Assign"));
            Assert.That(result.Xaml, Does.Contain("<WriteLine"));
            Assert.That(result.Xaml, Does.Not.Contain("x:TypeArguments=\"x:Object\""),
                "тип To/Value выведен, object в XAML не попал");
            Assert.That(result.Variables, Is.EqualTo(new[] { "output" }));
        });
    }

    [Test]
    public void NestedDocument_BuildsIfAndForEach()
    {
        JsonWorkflowResult result = Build(NestedDocument);

        Assert.That(result.Ok, Is.True, result.Describe());

        Assert.Multiple(() =>
        {
            Assert.That(result.Xaml, Does.Contain("<If"), "ветвление собрано");
            Assert.That(result.Xaml, Does.Contain("ForEach"), "цикл собран");
            Assert.That(result.Xaml, Does.Contain("ActivityAction"), "тело ForEach — ActivityAction<T>");
            Assert.That(result.Xaml, Does.Contain("Variable.Default"),
                "значение по умолчанию переменной сохранено выражением");
            Assert.That(result.ActivityCount, Is.GreaterThanOrEqualTo(3), "в дереве больше одной активности");
        });
    }

    [Test]
    public void Save_WritesFileThatLoadsBackAsActivity()
    {
        string directory = Path.Combine(WorkflowTestSamples.CreateTempDirectory(), "RPA-1");
        _temporaryDirectories.Add(Path.GetDirectoryName(directory));
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "Main.xaml");

        JsonWorkflowResult result = Create(new JsonWorkflowRequest { Json = SimpleDocument, FilePath = path });

        Assert.That(result.Ok, Is.True, result.Describe());
        Assert.That(File.Exists(path), Is.True, "файл процесса записан");

        Activity reloaded = ActivityXamlServices.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded, Is.Not.Null);
            Assert.That(result.WorkflowId, Is.EqualTo("RPA_1.Main"), "проект и класс выведены из пути");
            Assert.That(result.Opened, Is.False, "без среды конструктора вкладка не открывается");
            Assert.That(string.Join(" | ", result.Hints), Does.Contain("вкладка не открыта"));
        });
    }

    [Test]
    public void DocumentNameAndNamespace_OverrideDerivedValues()
    {
        string directory = Path.Combine(WorkflowTestSamples.CreateTempDirectory(), "RPA-1");
        _temporaryDirectories.Add(Path.GetDirectoryName(directory));
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "Main.xaml");

        const string document = @"{
  ""name"": ""Process"",
  ""namespace"": ""RPA_1.Automation"",
  ""project"": ""RPA-1"",
  ""activities"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""\""x\"""" } } ]
}";

        JsonWorkflowResult result = Create(new JsonWorkflowRequest { Json = document, FilePath = path });

        Assert.That(result.Ok, Is.True, result.Describe());

        Assert.Multiple(() =>
        {
            Assert.That(result.WorkflowId, Is.EqualTo("RPA_1.Automation.Process"));
            Assert.That(result.Xaml, Does.Contain("x:Class=\"RPA_1.Automation.Process\""));
        });
    }

    [Test]
    public void DocumentNamespacesAndReferences_GoToXaml()
    {
        const string document = @"{
  ""namespaces"": [ ""System.Text.RegularExpressions"" ],
  ""references"": [ ""System.Text.RegularExpressions"" ],
  ""activities"": [
    { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""Regex.IsMatch(\""a\"", \""a\"").ToString()"" } }
  ]
}";

        JsonWorkflowResult result = Build(document);

        Assert.That(result.Ok, Is.True, result.Describe());

        Assert.Multiple(() =>
        {
            Assert.That(result.Xaml, Does.Contain("System.Text.RegularExpressions"),
                "using из описания нужен конструктору для резолва выражений");
            Assert.That(result.Namespaces, Does.Contain("System.Text.RegularExpressions"));
            Assert.That(result.References, Does.Contain("System.Text.RegularExpressions"));
        });
    }

    [Test]
    public void ExplicitRoot_IsSupported()
    {
        const string document = @"{
  ""root"": {
    ""type"": ""Sequence"",
    ""children"": {
      ""Activities"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""\""x\"""" } } ]
    }
  }
}";

        JsonWorkflowResult result = Build(document);

        Assert.That(result.Ok, Is.True, result.Describe());
        Assert.That(result.Xaml, Does.Contain("<WriteLine"));
    }

    [Test]
    public void VariablesOnRoot_WithoutContainer_ReportError()
    {
        const string document = @"{
  ""variables"": [ { ""name"": ""data"", ""type"": ""String"" } ],
  ""root"": { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""\""x\"""" } }
}";

        JsonWorkflowResult result = Build(document);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("$.variables"));
        });
    }

    [Test]
    public void UnknownActivity_DoesNotWriteFile()
    {
        string directory = Path.Combine(WorkflowTestSamples.CreateTempDirectory(), "RPA-1");
        _temporaryDirectories.Add(Path.GetDirectoryName(directory));
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "Main.xaml");
        const string document = @"{ ""activities"": [ { ""type"": ""Teleport"" } ] }";

        JsonWorkflowResult result = Create(new JsonWorkflowRequest { Json = document, FilePath = path });

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("whitelist"));
            Assert.That(File.Exists(path), Is.False, "невалидное описание не оставляет файл в проекте");
        });
    }

    [Test]
    public void InvalidVariableType_DoesNotWriteFile()
    {
        string directory = Path.Combine(WorkflowTestSamples.CreateTempDirectory(), "RPA-1");
        _temporaryDirectories.Add(Path.GetDirectoryName(directory));
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "Main.xaml");
        const string document = @"{
  ""variables"": [ { ""name"": ""data"", ""type"": ""MyClass"" } ],
  ""activities"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""\""x\"""" } } ]
}";

        JsonWorkflowResult result = Create(new JsonWorkflowRequest { Json = document, FilePath = path });

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("не разрешён"));
            Assert.That(File.Exists(path), Is.False);
        });
    }

    [Test]
    public void MissingJson_ReportsSchemaHints()
    {
        JsonWorkflowResult result = Create(new JsonWorkflowRequest { FilePath = @"C:\wf\Main.xaml" });

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("xaml.ast"));
            Assert.That(string.Join(" | ", result.Hints), Does.Contain("activities"));
        });
    }

    [Test]
    public void WithoutProjectAndFilePath_Fails()
    {
        JsonWorkflowResult result = Create(new JsonWorkflowRequest { Json = SimpleDocument });

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("xaml.project"));
            Assert.That(string.Join(" | ", result.Hints), Does.Contain("project"));
        });
    }

    [Test]
    public void NotXamlPath_Fails()
    {
        JsonWorkflowResult result = Create(new JsonWorkflowRequest
        {
            Json = SimpleDocument,
            FilePath = Path.Combine(Path.GetTempPath(), "Main.cs")
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("xaml.path"));
        });
    }

    /// <summary>Сборка без записи файла: параметры класса задаются явно.</summary>
    private static JsonWorkflowResult Build(string json)
    {
        return Create(new JsonWorkflowRequest
        {
            Json = json,
            ProjectName = WorkflowTestSamples.ProjectName,
            NamespaceName = WorkflowTestSamples.WorkflowNamespace,
            ClassName = WorkflowTestSamples.WorkflowClass
        });
    }

    private static JsonWorkflowResult Create(JsonWorkflowRequest request)
    {
        return new JsonWorkflowService().Create(request);
    }
}
