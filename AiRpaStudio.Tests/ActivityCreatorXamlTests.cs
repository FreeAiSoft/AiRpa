using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using RPA.Models;

namespace AiRpaStudio.Tests;

/// <summary>
/// Регресс «Добавить активность»: из контекстного меню создаётся XAML-активность из встроенного
/// шаблона Visual Studio, а не C#-класс.
/// </summary>
[TestFixture]
public class ActivityCreatorXamlTests
{
    private string _root;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "activity_creator_xaml_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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

    [Test]
    public void Create_WritesXamlActivity_NotCsClass()
    {
        ActivityCreator creator = new ActivityCreator(_root);
        creator.ActivityName = "MyActivity";

        bool created = creator.Create();

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.True);
            Assert.That(creator.FilePath, Does.EndWith("MyActivity.xaml"));
            Assert.That(File.Exists(creator.FilePath), Is.True);
            Assert.That(Directory.GetFiles(_root, "*.cs"), Is.Empty, "XAML-активность не должна создавать C#-класс");
        });

        string xaml = File.ReadAllText(creator.FilePath);
        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("x:Class=\"RPA.MyActivity\""), "у встроенного проекта namespace по умолчанию RPA");
            Assert.That(xaml, Does.Contain("sap2010:ExpressionActivityEditor.ExpressionActivityEditor=\"C#\""), "выбрана ветка targetframeworkversion >= 4.5");
            Assert.That(xaml, Does.Not.Contain("$if$"));
            Assert.That(xaml, Does.Not.Contain("$endif$"));
            Assert.That(xaml, Does.Not.Contain("mva:VisualBasic"), "ветка < 4.5 (VisualBasic) должна быть отброшена");
        });
    }

    [Test]
    public void Create_VsProject_UsesRootNamespace()
    {
        string csproj = Path.Combine(_root, "MyLib.csproj");
        File.WriteAllText(csproj,
            "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><PropertyGroup><RootNamespace>MyLib</RootNamespace></PropertyGroup></Project>");

        ActivityCreator creator = new ActivityCreator(_root);
        creator.ActivityName = "MyActivity";
        _ = creator.Create();

        string xaml = File.ReadAllText(creator.FilePath);
        Assert.That(xaml, Does.Contain("x:Class=\"MyLib.MyActivity\""));
    }

    [Test]
    public void Create_ExistingFile_DoesNotOverwrite()
    {
        string existing = Path.Combine(_root, "MyActivity.xaml");
        File.WriteAllText(existing, "original");

        ActivityCreator creator = new ActivityCreator(_root);
        creator.ActivityName = "MyActivity";

        bool created = creator.Create();

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.False);
            Assert.That(File.ReadAllText(existing), Is.EqualTo("original"));
        });
    }

    [Test]
    public void BuildActivityXaml_ProcessesTemplateConditions()
    {
        string xaml = ActivityCreator.BuildActivityXaml("RPA", "Activity");

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("<Activity x:Class=\"RPA.Activity\""));
            Assert.That(xaml, Does.Contain("<TextExpression.NamespacesForImplementation>"));
            Assert.That(xaml, Does.Contain("<TextExpression.ReferencesForImplementation>"));
            Assert.That(xaml, Does.Not.Contain("$"));
            Assert.That(xaml, Does.Not.Contain("mva:VisualBasic"));
        });
    }

    [Test]
    public void EmbeddedTemplate_MatchesVsTemplateSource()
    {
        string resourceName = "RPA.VSTemplates.Activity.Activity.xaml";
        string embedded;
        using (Stream stream = typeof(ActivityCreator).Assembly.GetManifestResourceStream(resourceName))
        {
            Assert.That(stream, Is.Not.Null, "шаблон должен быть зашит в сборку как embedded resource");
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                embedded = reader.ReadToEnd();
            }
        }

        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "AiRpaStudio", "VSTemplates", "Activity", "Activity.xaml"));

        Assert.That(NormalizeLineEndings(embedded), Is.EqualTo(NormalizeLineEndings(source)),
            "встроенный шаблон обязан совпадать с исходным VS-шаблоном");
    }

    private static string NormalizeLineEndings(string text)
    {
        return (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").TrimStart('\uFEFF');
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