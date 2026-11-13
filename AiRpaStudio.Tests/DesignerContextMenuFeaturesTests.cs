using System;
using System.IO;
using AiRpaStudio.Tests.Design;
using NUnit.Framework;

namespace AiRpaStudio.Tests;

/// <summary>
/// Специфические фичи контекстного меню WF-дизайнера: «Точка останова» и «Аннотации».
/// <para>
/// Оба пункта — условные в <c>DesignerView</c>: их видимость вычисляется в <c>Loaded</c>-обработчиках
/// Reference Source. «Точка останова» показывается при <c>IDesignerDebugView</c> + <c>ICommandService</c>,
/// «Аннотации» — при <c>DesignerConfigurationService.AnnotationEnabled == true</c>. Если среда дизайнера не
/// выставляет эти условия, пункты «пропадают», хотя XAML меню на месте.
/// </para>
/// </summary>
public class DesignerContextMenuFeaturesTests
{
    [Test]
    public void DesignerEnvironment_EnablesAnnotations()
    {
        string source = ReadSource(@"AiRpaStudio\Helpers\WfDesignerEnvironment.cs");

        Assert.That(source, Does.Contain("configuration.AnnotationEnabled = true"),
            "аннотации в меню DesignerView видны только при AnnotationEnabled == true");
    }

    [Test]
    public void DesignerEnvironment_PublishesDesignerDebugView()
    {
        string source = ReadSource(@"AiRpaStudio\Helpers\WfDesignerEnvironment.cs");

        Assert.That(source, Does.Contain("designer.DebugManagerView"),
            "IDesignerDebugView публикуется лениво — его обязан форсировать конфигуратор среды");
    }

    [Test]
    public void DebuggerMappings_PublishesDebugViewBeforeMapping()
    {
        string source = ReadSource(@"AiRpaStudio\Helpers\DebuggerMappingsHelper.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("_ = workflowDesignerEx.DebugManagerView;"),
                "публикация DebugManagerView должна быть в начале генерации маппинга");
            Assert.That(source, Does.Contain("catch (System.Exception"),
                "«sequence not found» при поиске runtime-элемента не должен обрывать публикацию сервиса");
        });
    }

    [Test]
    public void StudioOpenPath_PublishesCommandService()
    {
        string source = ReadSource(@"AiRpaStudio\Helpers\LoadActivityToDesignerHelper.cs");

        Assert.That(source, Does.Contain("Publish<ICommandService>(commandService)"),
            "ICommandService публикует студийный путь открытия — без него «Точка останова» скрыта");
    }

    [Test]
    public void LooseXaml_ContextMenuFeatures_AreEnabled()
    {
        string rpa1 = FindRpa1();
        string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);

        DesignerFeaturesReport report = DesignerBenchProxy.Shared.DesignerFeaturesScenario(
            Path.Combine(rpa1, "Main.xaml"),
            xamlFiles);

        Assert.Multiple(() =>
        {
            Assert.That(report.Error, Is.Null.Or.Empty,
                "сценарий фич меню упал." + Environment.NewLine + report.Description);
            Assert.That(report.LoadSucceeded, Is.True, report.Description);
            Assert.That(report.AnnotationEnabled, Is.True,
                "без AnnotationEnabled пункт «Аннотации» в контекстном меню скрыт." + Environment.NewLine + report.Description);
            Assert.That(report.DebugViewAvailable, Is.True,
                "без IDesignerDebugView пункты «Точка останова» скрыты." + Environment.NewLine + report.Description);
            Assert.That(report.CommandServiceAvailable, Is.True,
                "без ICommandService пункты «Точка останова» скрыты." + Environment.NewLine + report.Description);
            Assert.That(report.BreakpointServicesEnabled, Is.True,
                "меню «Точка останова» требует оба сервиса." + Environment.NewLine + report.Description);
        });
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"не найден файл {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }

    private static string FindRpa1()
    {
        string root = FindRepositoryRoot();
        string rpa1 = Path.Combine(root, "RPA-1");
        Assert.That(Directory.Exists(rpa1), Is.True, "нет эталонного проекта RPA-1: " + rpa1);
        return rpa1;
    }
}