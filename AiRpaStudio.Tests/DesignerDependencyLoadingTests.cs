using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи поведения «вкладка остаётся открытой, пока готовятся зависимости NuGet-пакетов»:
/// вкладка добавляется до загрузки XAML, подготовка зависимостей идёт в фоне, а на вкладке
/// показывается indeterminate-кольцо MaterialDesign, управляемое <c>WorkflowDesignerEx.IsDependencyLoading</c>.
/// </summary>
[TestFixture]
public class DesignerDependencyLoadingTests
{
    [Test]
    public void LoadActivityToDesigner_PreparesDependenciesInBackgroundAndKeepsTab()
    {
        string source = ReadSource("AiRpaStudio", "Helpers", "LoadActivityToDesignerHelper.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("IsDependencyLoading = true"),
                "перед фоновой подготовкой зависимостей вкладка обязана показывать индикатор");
            Assert.That(source, Does.Contain("NuGetActivityPaletteLoader.EnsureDependencies"),
                "подготовка зависимостей обязана идти через EnsureDependencies");
            Assert.That(source, Does.Contain("System.Threading.Tasks.Task.Run"),
                "подготовка зависимостей обязана выполняться в фоне, а не на UI-потоке");
            Assert.That(source, Does.Contain("AddDesignerToStudio"),
                "вкладка обязана добавляться в студию до загрузки XAML");
        });
    }

    [Test]
    public void DocGroupHelper_ShowsMaterialDesignProgressWhileDependenciesLoad()
    {
        string source = ReadSource("AiRpaStudio", "Helpers", "DocGroupHelper.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("MaterialDesignCircularProgressBar"),
                "вкладка обязана показывать indeterminate-кольцо MaterialDesign");
            Assert.That(source, Does.Contain("nameof(WorkflowDesignerEx.IsDependencyLoading)"),
                "кольцо обязано подписываться на IsDependencyLoading");
        });
    }

    private static string ReadSource(params string[] parts)
    {
        string root = FindRepositoryRoot();
        string path = IOPath.Combine(new[] { root }.Concat(parts).ToArray());
        Assert.That(File.Exists(path), Is.True, "не найден исходник: " + path);
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(IOPath.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }
}
