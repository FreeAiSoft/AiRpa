using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Agent.Core.Contracts;
using Agent.Core.Models;
using NUnit.Framework;
using RPA.Chat.Services.Workflow;

namespace AiRpaStudio.Tests.Workflow;

/// <summary>
/// AG-08: служба создания активности (<c>ActivityCreationService</c>) — файлы <c>.cs</c> + дизайнер,
/// регистрация в <c>.csproj</c>, сборка и <b>откат</b> при любой ошибке.
/// <para>
/// Служба живёт в <c>RPA.Chat</c> и не зависит от WF/WPF: она пишет текст, правит XML проекта и дергает
/// <see cref="IBuildFeedbackService"/>. Поэтому весь набор проверяется на временной папке с настоящим
/// legacy-<c>.csproj</c> (TFM <c>v4.7.2</c>, как у библиотек активностей студии), без UI и без MSBuild.
/// </para>
/// <para>
/// Ключевая проверка карточки — атомарность: после неудачной сборки файлов активности быть не должно,
/// а <c>.csproj</c> обязан совпасть с исходным <b>побайтно</b> (формат, BOM и переводы строк — часть файла).
/// </para>
[TestFixture]
public class ActivityCreationServiceTests
{
    private string _root;
    private string _projectPath;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "activity_create_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _projectPath = Path.Combine(_root, "TestLibrary.csproj");
        File.WriteAllText(_projectPath, ProjectFile(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    [TearDown]
    public void TearDown()
    {
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
    // Файлы
    // ------------------------------------------------------------------

    [Test]
    public void Create_WritesClassFile_WithNamespaceBaseTypeAndArguments()
    {
        ActivityCreateRequest request = Request("SayHello");
        request.Description = "Печатает приветствие.";
        request.Arguments.Add(new ActivityCreateArgument { Name = "Text", TypeName = "String", Direction = "In", Required = true, Description = "Текст" });
        request.Arguments.Add(new ActivityCreateArgument { Name = "Count", TypeName = "Int32", Direction = "Out" });

        ActivityCreateResult result = Create(request);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.FullTypeName, Is.EqualTo("TestLibrary.SayHello"));
            Assert.That(result.ClassFile, Is.EqualTo(Path.Combine(_root, "SayHello.cs")));
        });

        string code = File.ReadAllText(result.ClassFile);
        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("namespace TestLibrary"));
            Assert.That(code, Does.Contain("public sealed class SayHello : CodeActivity"));
            Assert.That(code, Does.Contain("[RequiredArgument]"));
            Assert.That(code, Does.Contain("public InArgument<String> Text { get; set; }"));
            Assert.That(code, Does.Contain("public OutArgument<Int32> Count { get; set; }"));
            Assert.That(code, Does.Contain("protected override void Execute(CodeActivityContext context)"));
            Assert.That(code, Does.Contain("Печатает приветствие."));
        });
    }

    [Test]
    public void Create_WritesDesignerXamlAndCodeBehind()
    {
        ActivityCreateResult result = Create(Request("SayHello"));

        Assert.That(result.Succeeded, Is.True, result.Message);
        Assert.That(File.Exists(result.DesignerFile), Is.True, "нет файла дизайнера");
        Assert.That(File.Exists(Path.Combine(_root, "SayHelloDesigner.xaml.cs")), Is.True, "нет code-behind дизайнера");

        string designer = File.ReadAllText(result.DesignerFile);
        Assert.Multiple(() =>
        {
            Assert.That(designer, Does.Contain("x:Class=\"TestLibrary.SayHelloDesigner\""), "класс дизайнера в другом пространстве имён");
            Assert.That(designer, Does.Contain("sap:ActivityDesigner"));
            Assert.That(designer, Does.Contain("\r\n"), "дизайнер пишется с CRLF, как остальные файлы студии");
            Assert.That(designer, Does.Not.Contain("<?xml"), "XML-декларация в XAML не пишется");
        });
    }

    [Test]
    public void Create_WithoutDesigner_WritesOnlyClassFile()
    {
        ActivityCreateRequest request = Request("SayHello");
        request.WithDesigner = false;

        ActivityCreateResult result = Create(request);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.DesignerFile, Is.Null);
            Assert.That(File.Exists(Path.Combine(_root, "SayHelloDesigner.xaml")), Is.False);
            Assert.That(result.CreatedFiles.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void Create_WritesUtf8WithoutBom_AndCrlf()
    {
        ActivityCreateResult result = Create(Request("SayHello"));

        byte[] bytes = File.ReadAllBytes(result.ClassFile);
        Assert.Multiple(() =>
        {
            Assert.That(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, Is.False, "файл записан с BOM");
            Assert.That(File.ReadAllText(result.ClassFile), Does.Contain("\r\n"));
        });
    }

    // ------------------------------------------------------------------
    // .csproj
    // ------------------------------------------------------------------

    [Test]
    public void Create_RegistersFilesInCsproj_WithoutDuplicates()
    {
        Create(Request("First"));
        Create(Request("Second"));

        string xml = File.ReadAllText(_projectPath);
        Assert.Multiple(() =>
        {
            Assert.That(CountOf(xml, "Include=\"First.cs\""), Is.EqualTo(1));
            Assert.That(CountOf(xml, "Include=\"Second.cs\""), Is.EqualTo(1));
            Assert.That(CountOf(xml, "Include=\"FirstDesigner.xaml\""), Is.EqualTo(1));
            Assert.That(CountOf(xml, "Include=\"FirstDesigner.xaml.cs\""), Is.EqualTo(1), "code-behind дизайнера не зарегистрирован");
            Assert.That(xml, Does.Contain("<DependentUpon>FirstDesigner.xaml</DependentUpon>"));
            Assert.That(xml, Does.Contain("Properties\\AssemblyInfo.cs"), "старые элементы проекта потеряны");
        });
    }

    [Test]
    public void Create_KeepsCsprojFormat_BomAndLineEndings()
    {
        byte[] before = File.ReadAllBytes(_projectPath);

        Create(Request("SayHello"));

        byte[] after = File.ReadAllBytes(_projectPath);
        Assert.Multiple(() =>
        {
            Assert.That(after.Length > 3 && after[0] == 0xEF && after[1] == 0xBB && after[2] == 0xBF, Is.True, "BOM .csproj потерян");
            Assert.That(Encoding.UTF8.GetString(after), Does.Contain("\r\n"));
            Assert.That(Encoding.UTF8.GetString(after), Does.Not.Contain("\n\n\n"), "пустые строки «размножились»");
            Assert.That(before.Length, Is.LessThan(after.Length));
        });
    }

    [Test]
    public void Create_RollsBackFiles_WhenBuildFails()
    {
        byte[] before = File.ReadAllBytes(_projectPath);
        ActivityCreateRequest request = Request("SayHello");
        request.Build = true;

        var build = new FakeBuildFeedbackService
        {
            Errors = new List<BuildError>
            {
                new BuildError { Severity = "Error", Code = "CS0103", Message = "Имя 'X' не существует", File = "SayHello.cs", Line = 12 }
            }
        };

        ActivityCreateResult result = Create(request, build);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False, "неудачная сборка обязана считаться неудачей");
            Assert.That(result.Message, Does.Contain("CS0103"), "ошибки сборки не попали в ответ");
            Assert.That(File.Exists(Path.Combine(_root, "SayHello.cs")), Is.False, "файл класса не удалён при откате");
            Assert.That(File.Exists(Path.Combine(_root, "SayHelloDesigner.xaml")), Is.False, "дизайнер не удалён при откате");
            Assert.That(File.ReadAllBytes(_projectPath), Is.EqualTo(before), ".csproj не восстановлен побайтно");
        });
    }

    [Test]
    public void Create_ReportsAssemblyPath_WhenBuildSucceeds()
    {
        string outputDirectory = Path.Combine(_root, "bin", "Debug");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllBytes(Path.Combine(outputDirectory, "TestLibrary.dll"), new byte[] { 0x4D, 0x5A });

        ActivityCreateRequest request = Request("SayHello");
        request.Build = true;

        ActivityCreateResult result = Create(request, new FakeBuildFeedbackService());

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.AssemblyPath, Is.EqualTo(Path.Combine(outputDirectory, "TestLibrary.dll")));
            Assert.That(result.RequiresDecision, Is.Empty);
            Assert.That(File.Exists(Path.Combine(_root, "SayHello.cs")), Is.True);
        });
    }

    [Test]
    public void Create_WhenBuildSucceedsButAssemblyMissing_RequiresDecision()
    {
        ActivityCreateRequest request = Request("SayHello");
        request.Build = true;

        ActivityCreateResult result = Create(request, new FakeBuildFeedbackService());

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.AssemblyPath, Is.Null);
            Assert.That(string.Join(" ", result.RequiresDecision), Does.Contain("не найдена"),
                "молчание о ненайденной сборке — та же ошибка, что в AG-09");
        });
    }

    [Test]
    public void Create_AcceptsSdkStyleNet472Project()
    {
        File.WriteAllText(
            _projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n  <PropertyGroup>\r\n    <TargetFramework>net472</TargetFramework>\r\n    <RootNamespace>TestLibrary</RootNamespace>\r\n    <AssemblyName>TestLibrary</AssemblyName>\r\n  </PropertyGroup>\r\n</Project>\r\n");

        ActivityCreateResult result = Create(Request("SayHello"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(File.ReadAllText(_projectPath), Does.Contain("Include=\"SayHello.cs\""));
        });
    }

    // ------------------------------------------------------------------
    // Отказы
    // ------------------------------------------------------------------

    [Test]
    public void Create_RejectsProjectWithoutNet472()
    {
        File.WriteAllText(_projectPath, ProjectFile().Replace("<TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>", "<TargetFrameworkVersion>v4.6.1</TargetFrameworkVersion>"));

        ActivityCreateResult result = Create(Request("SayHello"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("net472"), "подсказка про TFM не названа");
            Assert.That(File.Exists(Path.Combine(_root, "SayHello.cs")), Is.False);
        });
    }

    [Test]
    public void Create_RejectsNonCsprojPath()
    {
        string solution = Path.Combine(_root, "TestLibrary.sln");
        File.WriteAllText(solution, "Microsoft Visual Studio Solution File");

        ActivityCreateResult result = Create(Request("SayHello", projectPath: solution));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain(".csproj"));
        });
    }

    [Test]
    public void Create_RejectsMissingProject()
    {
        ActivityCreateResult result = Create(Request("SayHello", projectPath: Path.Combine(_root, "Missing.csproj")));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("не найден").Or.Contain("not found"));
        });
    }

    [Test]
    public void Create_RejectsDuplicateClassName()
    {
        File.WriteAllText(Path.Combine(_root, "SayHello.cs"), "// уже есть");

        ActivityCreateResult result = Create(Request("SayHello"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("SayHello"));
            Assert.That(File.ReadAllText(Path.Combine(_root, "SayHello.cs")), Is.EqualTo("// уже есть"), "чужой файл перезаписан");
        });
    }

    [Test]
    public void Create_RejectsInvalidClassName()
    {
        ActivityCreateResult result = Create(Request("1 Bad Name"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("имя класса"));
        });
    }

    [Test]
    public void Create_RejectsBuildWithoutFeedbackService()
    {
        ActivityCreateRequest request = Request("SayHello");
        request.Build = true;

        ActivityCreateResult result = new ActivityCreationService(null).CreateAsync(request).GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("сборк"), "причина отказа не названа");
            Assert.That(File.Exists(Path.Combine(_root, "SayHello.cs")), Is.False, "файлы создались до проверки службы сборки");
        });
    }

    [Test]
    public void Create_UsesRootNamespaceFromProject_WhenNamespaceNotGiven()
    {
        File.WriteAllText(_projectPath, ProjectFile().Replace("<RootNamespace>TestLibrary</RootNamespace>", "<RootNamespace>My.Custom.Library</RootNamespace>"));

        ActivityCreateResult result = Create(Request("SayHello", projectPath: _projectPath));

        Assert.That(result.FullTypeName, Is.EqualTo("My.Custom.Library.SayHello"));
    }

    // ------------------------------------------------------------------
    // Инфраструктура
    // ------------------------------------------------------------------

    private ActivityCreateResult Create(ActivityCreateRequest request, IBuildFeedbackService build = null)
    {
        return new ActivityCreationService(build).CreateAsync(request).GetAwaiter().GetResult();
    }

    private ActivityCreateRequest Request(string className, string projectPath = null)
    {
        return new ActivityCreateRequest
        {
            ProjectPath = projectPath ?? _projectPath,
            ClassName = className
        };
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        int index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
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
            "    <Reference Include=\"System.Activities\" />",
            "  </ItemGroup>",
            "  <ItemGroup>",
            "    <Compile Include=\"Properties\\AssemblyInfo.cs\" />",
            "  </ItemGroup>",
            "  <Import Project=\"$(MSBuildToolsPath)\\Microsoft.CSharp.targets\" />",
            "</Project>",
            string.Empty
        });
    }

    private sealed class FakeBuildFeedbackService : IBuildFeedbackService
    {
        public IReadOnlyList<BuildError> Errors { get; set; } = new List<BuildError>();

        public Task BuildAsync(string configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> HasSolutionConfigurationAsync(string configuration, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<IReadOnlyList<BuildError>> GetCompilationErrorsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Errors);

        public Task<string> GetOutputWindowTextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
    }
}
