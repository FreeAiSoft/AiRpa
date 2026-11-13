using System;
using System.IO;

using NUnit.Framework;
using RPA.Core;

namespace AiRpaStudio.Tests;

/// <summary>
/// Ф1, шаг 2/5 — резолвер стартового скрипта <see cref="RunTargetResolver"/>: активная вкладка-дизайнер
/// побеждает, иначе настройка → главная активность → <c>Main.xaml</c>/<c>main.xaml</c> (регистронезависимо),
/// первый существующий <c>.xaml</c>, иначе <c>null</c>.
/// </summary>
[TestFixture]
public class RunTargetResolverTests
{
    private string _root;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "run_target_" + Guid.NewGuid().ToString("N"));
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
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Test]
    public void Resolve_ActiveWorkflowDesignerTab_WinsOverConfiguredDefault()
    {
        string sub = WriteXaml("SubProcess1.xaml");

        string result = RunTargetResolver.Resolve(
            _root,
            configuredDefault: "Main.xaml",
            projectMainActivity: "Main.xaml",
            activeDocumentPath: sub,
            getOpenWorkflowPath: path => string.Equals(path, sub, StringComparison.OrdinalIgnoreCase) ? sub : null);

        Assert.That(result, Is.EqualTo(sub),
            "открытый WF-дизайнер активной вкладки обязан побеждать настройку");
    }

    [Test]
    public void Resolve_CodeEditorTab_IsNotWorkflowDesignerAndFallsBackToConfiguredDefault()
    {
        string custom = WriteXaml("Custom.xaml");
        string activeCodeFile = Path.Combine(_root, "Program.cs");
        File.WriteAllText(activeCodeFile, "// not a workflow");

        string result = RunTargetResolver.Resolve(
            _root,
            configuredDefault: "Custom.xaml",
            projectMainActivity: "Main.xaml",
            activeDocumentPath: activeCodeFile,
            getOpenWorkflowPath: _ => null);

        Assert.That(result, Is.EqualTo(custom),
            "вкладка редактора кода не является WF-дизайнером — должен сработать стартовый скрипт из настройки");
    }

    [Test]
    public void Resolve_NoActiveTab_UsesConfiguredDefault()
    {
        string custom = WriteXaml("Custom.xaml");

        string result = RunTargetResolver.Resolve(
            _root,
            configuredDefault: "Custom.xaml",
            projectMainActivity: "Main.xaml",
            activeDocumentPath: null,
            getOpenWorkflowPath: null);

        Assert.That(result, Is.EqualTo(custom));
    }

    [Test]
    public void Resolve_EmptyConfiguredDefault_UsesProjectMainActivity()
    {

        string main = WriteXaml("Main.xaml");
        string result = RunTargetResolver.Resolve(
            _root,
            configuredDefault: string.Empty,
            projectMainActivity: "Main.xaml",
            activeDocumentPath: null,
            getOpenWorkflowPath: null);

        Assert.That(result, Is.EqualTo(main),
            "если настройка пуста, запускается главная активность проекта");
    }

    [Test]
    public void Resolve_FallsBackToMainXaml_WhenNothingConfigured()
    {

        string main = WriteXaml("Main.xaml");
        string result = RunTargetResolver.Resolve(
            _root,
            configuredDefault: null,
            projectMainActivity: null,
            activeDocumentPath: null,
            getOpenWorkflowPath: null);

        Assert.That(result, Is.EqualTo(main));
    }

    [Test]
    public void Resolve_FallsBackToMainXaml_CaseInsensitive()
    {
        string main = WriteXaml("main.xaml");

        string result = RunTargetResolver.Resolve(
            _root,
            configuredDefault: null,
            projectMainActivity: null,
            activeDocumentPath: null,
            getOpenWorkflowPath: null);

        Assert.That(result, Is.Not.Null, "фолбэк Main.xaml/main.xaml обязан найти файл");
        Assert.That(File.Exists(result), Is.True);
        // На Windows файловая система регистронезависима: первый кандидат Main.xaml уже совпадает
        // со строчным main.xaml, поэтому проверяем не строку, а тот же файл.
        Assert.That(Path.GetFullPath(result), Is.EqualTo(Path.GetFullPath(main)).IgnoreCase);
    }

    [Test]
    public void Resolve_ConfiguredDefault_IsCaseInsensitive()
    {
        WriteXaml("Main.xaml");

        string result = RunTargetResolver.Resolve(
            _root,
            configuredDefault: "MAIN.XAML",
            projectMainActivity: null,
            activeDocumentPath: null,
            getOpenWorkflowPath: null);

        Assert.That(result, Is.Not.Null, "поиск обязан быть регистронезависимым");
        Assert.That(File.Exists(result), Is.True);
        Assert.That(Path.GetFileName(result), Is.EqualTo("MAIN.XAML"),
            "путь собирается из кандидата как есть (Path.Combine/корневой)");
    }

    [Test]
    public void Resolve_RootedConfiguredDefault_IsUsedAsIs()
    {
        string nested = WriteXaml("nested/Start.xaml");

        string result = RunTargetResolver.Resolve(
            _root,
            configuredDefault: nested,
            projectMainActivity: null,
            activeDocumentPath: null,
            getOpenWorkflowPath: null);

        Assert.That(result, Is.EqualTo(nested),
            "корневой кандидат не должен дополнительно комбинироваться с projectDir");
    }

    [Test]
    public void Resolve_RelativeConfiguredDefault_InSubfolder_IsCombinedWithProjectDir()
    {
        string start = WriteXaml("flows/Start.xaml");

        string result = RunTargetResolver.Resolve(
            _root,
            configuredDefault: "flows/Start.xaml",
            projectMainActivity: null,
            activeDocumentPath: null,
            getOpenWorkflowPath: null);

        Assert.That(result, Is.EqualTo(start));
    }

    [Test]
    public void Resolve_NoExistingCandidate_ReturnsNull()
    {
        string result = RunTargetResolver.Resolve(
            _root,
            configuredDefault: "Missing.xaml",
            projectMainActivity: "AlsoMissing.xaml",
            activeDocumentPath: null,
            getOpenWorkflowPath: null);

        Assert.That(result, Is.Null,
            "студия должна получить null и показать понятное сообщение, а не падать");
    }

    private string WriteXaml(string relativePath)
    {
        string path = Path.Combine(_root, relativePath);
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, "<Activity />");
        return path;
    }
}
