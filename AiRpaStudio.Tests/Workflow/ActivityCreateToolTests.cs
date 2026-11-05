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
using NUnit.Framework;
using RPA.Chat.Services;
using RPA.Chat.Services.AgentTools;
using RPA.Chat.Services.Workflow;

namespace AiRpaStudio.Tests.Workflow;

/// <summary>
/// AG-08: инструмент <c>activity_create</c> — 14-й WF-инструмент агента.
/// <para>
/// Проверяется то, чего не видно в тестах службы: регистрация и режимы (мутирующий, <c>EditFiles</c>,
/// в Plan не виден), обязательные аргументы, тексты отказов и <b>связка с палитрой</b>: обновление
/// «Активностей» вызывается ровно один раз и только после успешной сборки.
/// </para>
/// </summary>
[TestFixture]
public class ActivityCreateToolTests
{
    private string _root;
    private string _projectPath;
    private IActivityPaletteNotifier _previousNotifier;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "activity_create_tool_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _projectPath = Path.Combine(_root, "TestLibrary.csproj");
        File.WriteAllText(_projectPath, ProjectFile(), new UTF8Encoding(true));
        _previousNotifier = ChatEnvironment.ActivityPaletteNotifier;
    }

    [TearDown]
    public void TearDown()
    {
        ChatEnvironment.ActivityPaletteNotifier = _previousNotifier;

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    // ------------------------------------------------------------------
    // Реестр и режимы
    // ------------------------------------------------------------------

    [Test]
    public void ActivityCreate_IsRegisteredAsMutatingWorkflowTool()
    {
        AgentToolRegistry registry = CreateFactory().CreateRegistry();

        IAgentTool tool = registry.Find("activity_create");
        Assert.That(tool, Is.Not.Null, "нет инструмента activity_create");

        Assert.Multiple(() =>
        {
            Assert.That(tool.Mutates, Is.True, "создание файлов проекта — мутирующий инструмент");
            Assert.That(tool.Permission, Is.EqualTo(AgentPermissions.EditFiles));
            Assert.That(registry.ForMode(AgentMode.Plan, AgentPermissions.All).Select(item => item.Name),
                Does.Not.Contain("activity_create"), "в режиме Plan активность создавать нельзя");
            Assert.That(registry.ForMode(AgentMode.Act, AgentPermissions.All).Select(item => item.Name),
                Does.Contain("activity_create"));
            Assert.That(tool.Description, Does.Not.Contain("mcp"), "в описании не должно быть категории MCP");
        });
    }

    // ------------------------------------------------------------------
    // Отказы
    // ------------------------------------------------------------------

    [Test]
    public void ActivityCreate_RequiresProjectPath()
    {
        string result = Run(new JObject { ["className"] = "SayHello" });

        Assert.That(result, Does.Contain("projectPath"));
    }

    [Test]
    public void ActivityCreate_RejectsMissingProject()
    {
        string result = Run(Arguments(Path.Combine(_root, "Missing.csproj"), "SayHello"));

        Assert.That(result, Does.Contain("не найден").Or.Contain("not found"));
    }

    [Test]
    public void ActivityCreate_RejectsNonCsproj()
    {
        string solution = Path.Combine(_root, "TestLibrary.sln");
        File.WriteAllText(solution, "Microsoft Visual Studio Solution File");

        string result = Run(Arguments(solution, "SayHello"));

        Assert.That(result, Does.Contain(".csproj"));
    }

    [Test]
    public void ActivityCreate_RejectsDuplicateClass()
    {
        File.WriteAllText(Path.Combine(_root, "SayHello.cs"), "// уже есть");

        string result = Run(Arguments(_projectPath, "SayHello"));

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("SayHello"));
            Assert.That(File.ReadAllText(Path.Combine(_root, "SayHello.cs")), Is.EqualTo("// уже есть"));
        });
    }

    [Test]
    public void ActivityCreate_RejectsInvalidName()
    {
        string result = Run(Arguments(_projectPath, "1 Bad Name"));

        Assert.That(result, Does.Contain("имя класса"));
    }

    // ------------------------------------------------------------------
    // Успешное создание
    // ------------------------------------------------------------------

    [Test]
    public void ActivityCreate_CreatesFiles_AndRegistersThemInProject()
    {
        ChatEnvironment.ActivityPaletteNotifier = new RecordingNotifier();

        JObject arguments = Arguments(_projectPath, "SayHello");
        arguments["description"] = "Печатает приветствие";
        arguments["arguments"] = new JArray(new JObject
        {
            ["name"] = "Text",
            ["typeName"] = "String",
            ["direction"] = "In",
            ["required"] = true
        });

        string result = Run(arguments);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("TestLibrary.SayHello"));
            Assert.That(result, Does.Contain("SayHello.cs"));
            Assert.That(result, Does.Contain("SayHelloDesigner.xaml"));
            Assert.That(result, Does.Contain("TestLibrary.csproj"));
            Assert.That(result, Does.Not.Contain("ошибка"));
        });

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(_root, "SayHello.cs")), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(_root, "SayHello.cs")), Does.Contain("InArgument<String> Text"));
            Assert.That(File.ReadAllText(_projectPath), Does.Contain("Include=\"SayHello.cs\""));
        });
    }

    [Test]
    public void ActivityCreate_WithoutBuild_DoesNotNotifyPalette()
    {
        var notifier = new RecordingNotifier();
        ChatEnvironment.ActivityPaletteNotifier = notifier;

        string result = Run(Arguments(_projectPath, "SayHello"));

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("сборка"), "ответ обязан сказать, что сборки не было");
            Assert.That(notifier.Calls, Is.EqualTo(0), "палитра обновлялась без сборки");
        });
    }

    [Test]
    public void ActivityCreate_RollsBackOnBuildFailure_AndDoesNotNotifyPalette()
    {
        var notifier = new RecordingNotifier();
        ChatEnvironment.ActivityPaletteNotifier = notifier;

        JObject arguments = Arguments(_projectPath, "SayHello");
        arguments["build"] = true;

        string result = Run(arguments, buildErrors: new List<BuildError>
        {
            new BuildError { Severity = "Error", Code = "CS0103", Message = "Имя 'X' не существует" }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("CS0103"));
            Assert.That(File.Exists(Path.Combine(_root, "SayHello.cs")), Is.False, "откат не выполнен");
            Assert.That(notifier.Calls, Is.EqualTo(0), "палитра обновлялась после неудачной сборки");
        });
    }

    [Test]
    public void ActivityCreate_NotifiesPaletteOnce_WhenBuildSucceeds()
    {
        string outputDirectory = Path.Combine(_root, "bin", "Debug");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllBytes(Path.Combine(outputDirectory, "TestLibrary.dll"), new byte[] { 0x4D, 0x5A });

        var notifier = new RecordingNotifier();
        ChatEnvironment.ActivityPaletteNotifier = notifier;

        JObject arguments = Arguments(_projectPath, "SayHello");
        arguments["build"] = true;

        string result = Run(arguments);

        Assert.Multiple(() =>
        {
            Assert.That(notifier.Calls, Is.EqualTo(1), "палитра обязана обновиться ровно один раз");
            Assert.That(notifier.LastAssemblyPath, Is.EqualTo(Path.Combine(outputDirectory, "TestLibrary.dll")));
            Assert.That(result, Does.Contain("палитра"));
            Assert.That(result, Does.Contain("3"), "в ответе нет числа добавленных типов");
        });
    }

    [Test]
    public void ActivityCreate_ReportsPaletteNotPerformed()
    {
        string outputDirectory = Path.Combine(_root, "bin", "Debug");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllBytes(Path.Combine(outputDirectory, "TestLibrary.dll"), new byte[] { 0x4D, 0x5A });

        // Хост студии палитру не подключил — инструмент обязан сказать это словами, а не молчать.
        ChatEnvironment.ActivityPaletteNotifier = NullActivityPaletteNotifier.Instance;

        JObject arguments = Arguments(_projectPath, "SayHello");
        arguments["build"] = true;

        string result = Run(arguments);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("палитра"));
            Assert.That(result, Does.Contain("не обновлена").Or.Contain("не подключ"));
            Assert.That(result, Does.Not.Contain("палитра: обновлена"));
        });
    }

    // ------------------------------------------------------------------
    // Инфраструктура
    // ------------------------------------------------------------------

    private JObject Arguments(string projectPath, string className)
    {
        return new JObject
        {
            ["projectPath"] = projectPath,
            ["className"] = className
        };
    }

    private string Run(JObject arguments, IReadOnlyList<BuildError> buildErrors = null)
    {
        AgentToolRegistry registry = CreateFactory(buildErrors).CreateRegistry();
        IAgentTool tool = registry.Find("activity_create");
        Assert.That(tool, Is.Not.Null, "нет инструмента activity_create");

        return tool.ExecuteAsync(arguments, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static AgentToolFactory CreateFactory(IReadOnlyList<BuildError> buildErrors = null)
    {
        return new AgentToolFactory(
            new StubAiProvider(),
            () => Path.GetTempPath(),
            NullStudioDocumentService.Instance,
            new FakeBuildFeedbackService(buildErrors),
            (question, options, ct) => Task.FromResult("answer"),
            (systemPrompt, task, ct) => Task.FromResult("sub-result"),
            enableSubagents: false,
            subagentMaxDepth: 2);
    }

    private static string ProjectFile()
    {
        return string.Join("\r\n", new[]
        {
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
            "<Project ToolsVersion=\"15.0\" DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">",
            "  <PropertyGroup>",
            "    <OutputType>Library</OutputType>",
            "    <RootNamespace>TestLibrary</RootNamespace>",
            "    <AssemblyName>TestLibrary</AssemblyName>",
            "    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>",
            "  </PropertyGroup>",
            "  <PropertyGroup Condition=\" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' \">",
            "    <OutputPath>bin\\Debug\\</OutputPath>",
            "  </PropertyGroup>",
            "  <ItemGroup>",
            "    <Compile Include=\"Properties\\AssemblyInfo.cs\" />",
            "  </ItemGroup>",
            "  <Import Project=\"$(MSBuildToolsPath)\\Microsoft.CSharp.targets\" />",
            "</Project>",
            string.Empty
        });
    }

    /// <summary>Заглушка палитры: считает вызовы и запоминает путь сборки.</summary>
    private sealed class RecordingNotifier : IActivityPaletteNotifier
    {
        public int Calls { get; private set; }

        public string LastAssemblyPath { get; private set; }

        public ActivityPaletteRefreshResult Notify(string assemblyPath)
        {
            Calls++;
            LastAssemblyPath = assemblyPath;
            return ActivityPaletteRefreshResult.From(Path.GetFileNameWithoutExtension(assemblyPath), 3);
        }
    }

    private sealed class FakeBuildFeedbackService : IBuildFeedbackService
    {
        private readonly IReadOnlyList<BuildError> _errors;

        public FakeBuildFeedbackService(IReadOnlyList<BuildError> errors)
        {
            _errors = errors ?? new List<BuildError>();
        }

        public Task BuildAsync(string configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> HasSolutionConfigurationAsync(string configuration, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<IReadOnlyList<BuildError>> GetCompilationErrorsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_errors);

        public Task<string> GetOutputWindowTextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
    }
}
