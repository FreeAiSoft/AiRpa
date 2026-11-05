using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Agent.Core.AgentTools;
using Agent.Core.Contracts;
using Agent.Core.Models;
using Agent.Core.Services;
using Newtonsoft.Json.Linq;
using RPA.Chat.Services;
using RPA.Chat.Services.AgentTools;
using RPA.Chat.Services.Workflow;

namespace AiRpaStudio.Tests.Workflow;

/// <summary>
/// AG-07: <c>editor</c> как единственный путь текстовой правки XAML (отдельного <c>xaml_write</c> нет).
/// <para>
/// Проверяется то, что нельзя увидеть в нормализаторе: файл на диске, BOM, отказ «открыт в конструкторе»,
/// режимы валидации <c>off</c>/<c>report</c>/<c>strict</c> и откат в <c>strict</c>. Гейт и мост правок —
/// фейки: здесь важен контракт, живой конструктор проверяется отдельно в
/// <see cref="WorkflowXamlBridgeTests"/>.
/// </para>
/// </summary>
[TestFixture]
public class WorkflowEditorXamlTests
{
    private const string ActivitiesNs = "http://schemas.microsoft.com/netfx/2009/xaml/activities";
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    private string _root;
    private FakeWorkflowEditingService _editing;
    private FakeWorkflowXamlGate _gate;

    [SetUp]
    public void CreateWorkspace()
    {
        _root = Path.Combine(Path.GetTempPath(), "airpa-editor-xaml-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _editing = new FakeWorkflowEditingService();
        _gate = new FakeWorkflowXamlGate();
    }

    [TearDown]
    public void RemoveWorkspace()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (Exception)
        {
            // Каталог может остаться заблокированным — на результат прогона это не влияет.
        }
    }

    private string PathOf(string name)
    {
        return Path.Combine(_root, name);
    }

    private static string Workflow(string inner)
    {
        return "<Activity xmlns=\"" + ActivitiesNs + "\" xmlns:x=\"" + XamlNs + "\">\r\n  " + inner + "\r\n</Activity>";
    }

    private AgentToolFactory CreateFactory()
    {
        return new AgentToolFactory(
            new StubAiProvider(),
            () => _root,
            NullStudioDocumentService.Instance,
            new FakeBuildFeedbackService(),
            (question, options, ct) => Task.FromResult("answer"),
            (systemPrompt, task, ct) => Task.FromResult("sub-result"),
            enableSubagents: false,
            subagentMaxDepth: 2,
            getDocsDirectory: null,
            activityCatalog: null,
            workflowOutline: null,
            workflowEditing: _editing,
            workflowXamlGate: _gate);
    }

    private string Run(string toolName, JObject arguments)
    {
        AgentToolRegistry registry = CreateFactory().CreateRegistry();
        IAgentTool tool = registry.Find(toolName);
        Assert.That(tool, Is.Not.Null, "нет инструмента " + toolName);

        return tool.ExecuteAsync(arguments, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static JObject Write(string path, string content, string validate = null)
    {
        var args = new JObject { ["path"] = path, ["content"] = content };
        if (validate != null)
        {
            args["validate"] = validate;
        }

        return args;
    }

    [Test]
    public void Editor_NormalizesWorkflowXamlOnWrite()
    {
        string path = PathOf("Main.xaml");

        string result = Run("editor", Write(path, Workflow("<Sequence />")));
        string written = File.ReadAllText(path);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("File written"));
            Assert.That(written, Does.Contain("xmlns:mc=\""), "нормализация добавляет пространства имён студии");
            Assert.That(written, Does.Contain("xmlns:mca=\""), "mca нужен для C#-выражений");
            Assert.That(written, Does.Contain("mc:Ignorable=\"sap sap2010\""));
            Assert.That(result, Does.Contain("normalized"), "ответ обязан сказать, что файл нормализован");
            Assert.That(_gate.Calls, Is.EqualTo(1), "режим report по умолчанию обязан прогонять валидацию");
        });
    }

    [Test]
    public void Editor_PreservesBomOfExistingFile()
    {
        string path = PathOf("Main.xaml");
        byte[] bom = { 0xEF, 0xBB, 0xBF };
        File.WriteAllBytes(path, bom.Concat(Encoding.UTF8.GetBytes(Workflow("<Sequence />"))).ToArray());

        string result = Run("editor", Write(path, Workflow("<Sequence><WriteLine Text=\"a\" /></Sequence>")));
        byte[] written = File.ReadAllBytes(path);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("File written"));
            Assert.That(written.Take(3), Is.EqualTo(bom), "BOM существующего файла сохраняется (иначе diff на весь файл)");
        });
    }

    [Test]
    public void Editor_WritesNewXamlFileWithoutBom()
    {
        string path = PathOf("Created.xaml");

        Run("editor", Write(path, Workflow("<Sequence />")));
        byte[] written = File.ReadAllBytes(path);

        Assert.That(written.Take(3), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }),
            "новый файл пишется как файлы студии — UTF-8 без BOM");
    }

    [Test]
    public void Editor_LeavesNonXamlFilesUntouched()
    {
        string path = PathOf("Activity.cs");
        string content = "class A\r\n{\r\n\tvoid M() { }\n}\n";

        string result = Run("editor", Write(path, content));

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("File written"));
            Assert.That(File.ReadAllText(path), Is.EqualTo(content),
                "не-XAML текст не нормализуется: EOL и отступы остаются как были");
            Assert.That(File.ReadAllText(path), Does.Not.Contain("xmlns:mc"));
            Assert.That(_gate.Calls, Is.Zero, "валидация XAML не применяется к .cs");
        });
    }

    [Test]
    public void Editor_ReplacesTextInXamlAndNormalizesResult()
    {
        string path = PathOf("Main.xaml");
        File.WriteAllText(path, Workflow("<WriteLine Text=\"first\" />"));

        string result = Run("editor", new JObject
        {
            ["path"] = path,
            ["old_text"] = "Text=\"first\"",
            ["new_text"] = "Text=\"second\""
        });

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("File edited"));
            Assert.That(File.ReadAllText(path), Does.Contain("Text=\"second\""));
            Assert.That(File.ReadAllText(path), Does.Contain("xmlns:mc=\""), "результат замены тоже нормализуется");
        });
    }

    [Test]
    public void Editor_RefusesMalformedXamlWithoutWriting()
    {
        string path = PathOf("Main.xaml");
        const string valid = "<Activity xmlns=\"" + ActivitiesNs + "\">\r\n  <Sequence />\r\n</Activity>";
        File.WriteAllText(path, valid);

        string result = Run("editor", Write(path, "<Activity><Sequence></Activity>"));

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("File not written"));
            Assert.That(result, Does.Contain("not well-formed XML"));
            Assert.That(File.ReadAllText(path), Is.EqualTo(valid), "битый XAML не попадает на диск");
            Assert.That(_gate.Calls, Is.Zero, "нечего валидировать: файл не записан");
        });
    }

    [Test]
    public void Editor_RefusesWhenFileIsOpenInDesigner()
    {
        string path = PathOf("Main.xaml");
        _editing.OpenInDesigner.Add(path);

        string result = Run("editor", Write(path, Workflow("<Sequence />")));

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("File not written"));
            Assert.That(result, Does.Contain("open in the studio designer"));
            Assert.That(result, Does.Contain("activity_insert"), "агент получает подсказку, чем править вместо текста");
            Assert.That(File.Exists(path), Is.False, "запись в открытый конструктором файл не выполняется");
        });
    }

    [Test]
    public void Editor_ReportsValidationErrorsButWritesFile()
    {
        string path = PathOf("Main.xaml");
        _gate.Result = WorkflowXamlGateResult.From(
            new[] { "shape.xClass: у корня нет x:Class" },
            new string[0]);

        string result = Run("editor", Write(path, Workflow("<Sequence />")));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(path), Is.True, "в режиме report файл записывается");
            Assert.That(result, Does.Contain("validation: 1 error"));
            Assert.That(result, Does.Contain("shape.xClass"));
        });
    }

    [Test]
    public void Editor_StrictModeRestoresPreviousContentOnValidationErrors()
    {
        string path = PathOf("Main.xaml");
        const string previous = "<Activity xmlns=\"" + ActivitiesNs + "\">\r\n  <Sequence DisplayName=\"old\" />\r\n</Activity>";
        File.WriteAllText(path, previous);
        _gate.Result = WorkflowXamlGateResult.From(new[] { "wf.validation: Required argument 'Text' is not set" }, new string[0]);

        string result = Run("editor", Write(path, Workflow("<WriteLine />"), validate: "strict"));

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("Write reverted"));
            Assert.That(result, Does.Contain("Required argument"), "причина отката обязана быть видна");
            Assert.That(File.ReadAllText(path), Is.EqualTo(previous), "в strict файл возвращается к прежнему содержимому");
        });
    }

    [Test]
    public void Editor_StrictModeDeletesNewFileOnValidationErrors()
    {
        string path = PathOf("New.xaml");
        _gate.Result = WorkflowXamlGateResult.From(new[] { "wf.validation: bad" }, new string[0]);

        string result = Run("editor", Write(path, Workflow("<WriteLine />"), validate: "strict"));

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("Write reverted"));
            Assert.That(File.Exists(path), Is.False, "созданный файл, не прошедший strict-валидацию, удаляется");
        });
    }

    [Test]
    public void Editor_NotPerformedValidationIsNotReportedAsNoErrors()
    {
        string path = PathOf("Main.xaml");
        _gate.Result = WorkflowXamlGateResult.NotPerformed("гейт валидации не подключён хостом студии");

        string result = Run("editor", Write(path, Workflow("<Sequence />")));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(path), Is.True);
            Assert.That(result, Does.Contain("not performed"));
            Assert.That(result, Does.Not.Contain("0 errors"),
                "«валидация не выполнена» не должно выглядеть как «ошибок нет»");
        });
    }

    [Test]
    public void Editor_SkipsValidationWhenOff()
    {
        string path = PathOf("Main.xaml");

        string result = Run("editor", Write(path, Workflow("<Sequence />"), validate: "off"));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(path), Is.True);
            Assert.That(_gate.Calls, Is.Zero, "validate=off не зовёт гейт");
            Assert.That(result, Does.Contain("skipped"));
        });
    }

    [Test]
    public void Editor_RejectsUnknownValidateModeWithoutWriting()
    {
        string path = PathOf("Main.xaml");

        string result = Run("editor", Write(path, Workflow("<Sequence />"), validate: "sometimes"));

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("Invalid \"validate\""));
            Assert.That(File.Exists(path), Is.False, "неизвестный режим валидации — отказ до записи");
        });
    }

    [Test]
    public void ReadFiles_AddsXamlHeaderForWorkflowFiles()
    {
        string path = PathOf("Main.xaml");
        File.WriteAllText(path, Workflow("<Sequence />"), new UTF8Encoding(false));

        string result = Run("read_files", new JObject { ["path"] = path });

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("xaml:"), "у .xaml есть шапка метаданных");
            Assert.That(result, Does.Contain("UTF-8 без BOM"));
            Assert.That(result, Does.Contain("EOL CRLF"));
            Assert.That(result, Does.Contain("в конструкторе: нет"));
            Assert.That(result, Does.Contain("<Sequence />"), "содержимое файла остаётся в ответе");
        });
    }

    [Test]
    public void ReadFiles_DoesNotAddXamlHeaderForOtherFiles()
    {
        string path = PathOf("Activity.cs");
        File.WriteAllText(path, "class A { }");

        string result = Run("read_files", new JObject { ["path"] = path });

        Assert.That(result, Does.Not.Contain("UTF-8 без BOM"));
        Assert.That(result, Does.Not.Contain("EOL CRLF"));
    }

    private sealed class FakeWorkflowXamlGate : IWorkflowXamlGate
    {
        public int Calls { get; private set; }

        public string LastXaml { get; private set; }

        public WorkflowXamlGateResult Result { get; set; } =
            WorkflowXamlGateResult.From(new string[0], new string[0]);

        public WorkflowXamlGateResult ValidateText(string xaml)
        {
            Calls++;
            LastXaml = xaml;
            return Result;
        }
    }

    private sealed class FakeWorkflowEditingService : IWorkflowEditingService
    {
        public readonly HashSet<string> OpenInDesigner = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool IsOpenInDesigner(string path)
        {
            return path != null && OpenInDesigner.Contains(path);
        }

        public IWorkflowEditSession BeginEdit(string path, string label = null)
        {
            return new UnavailableWorkflowEditSession(path, label, "файл не открыт в конструкторе");
        }

        public WorkflowEditResult Undo(string path)
        {
            return WorkflowEditResult.Failure("undo", "файл не открыт в конструкторе");
        }

        public WorkflowEditResult Redo(string path)
        {
            return WorkflowEditResult.Failure("redo", "файл не открыт в конструкторе");
        }

        public WorkflowOutline GetLiveOutline(string path, int depth = 0, bool includeArguments = true)
        {
            return null;
        }
    }

    private sealed class FakeBuildFeedbackService : IBuildFeedbackService
    {
        public Task BuildAsync(string configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> HasSolutionConfigurationAsync(string configuration, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<IReadOnlyList<BuildError>> GetCompilationErrorsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BuildError>>(new List<BuildError>());

        public Task<string> GetOutputWindowTextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
    }
}
