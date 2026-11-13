using System;
using System.IO;

using NUnit.Framework;

namespace AiRpaStudio.Tests;

/// <summary>
/// Ф1, шаг 3 — стражи точек сборки кнопки «Старт» и поиска открытого дизайнера. Сама логика выбора
/// проверяется в <see cref="RunTargetResolverTests"/>; здесь — что кнопка спрашивает резолвер, а не
/// запускает <c>ActiveWorkflowDesignerEx.CurrentWorkflowFile</c>, и что поиск открытого дизайнера идёт
/// без учёта регистра.
/// </summary>
public class RunTargetWiringTests
{
    [Test]
    public void WorkflowRun_ResolvesTargetInsteadOfRunningActiveDesignerFile()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Core\CustomWfDesigner.cs"), "WorkflowRun");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("ResolveRunTarget()"),
                "кнопка «Старт» должна спрашивать резолвер стартового скрипта");
            Assert.That(method, Does.Not.Contain("CurrentWorkflowFile"),
                "запуск файла активного дизайнера напрямую — и есть дефект П1: запускался не тот скрипт");
            Assert.That(method, Does.Contain("_activityRunner.Run(runPath)"),
                "резолвер возвращает путь, который и запускает раннер");
        });
    }

    [Test]
    public void ResolveRunTarget_UsesRunTargetResolver()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Core\CustomWfDesigner.cs"), "ResolveRunTarget");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("RunTargetResolver.Resolve"),
                "выбор стартового скрипта обязан идти через чистый резолвер");
            Assert.That(method, Does.Contain("DefaultRunScript"),
                "настройка «Стартовый скрипт» должна попадать в резолвер");
            Assert.That(method, Does.Contain("GetOpenWorkflowPath"),
                "активная вкладка должна маппиться в открытый WF-дизайнер");
        });
    }

    [Test]
    public void GetDesignerByFileIgnoreCase_IsCaseInsensitive()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Core\CustomWfDesigner.cs"), "GetDesignerByFileIgnoreCase");

        Assert.That(method, Does.Contain("OrdinalIgnoreCase"),
            "поиск открытого дизайнера обязан быть регистронезависимым");
    }

    [Test]
    public void WorkflowRun_TreatsStartingErrorAsTerminalAndStartsNewRun()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Core\CustomWfDesigner.cs"), "WorkflowRun");

        Assert.That(method, Does.Contain("WorkflowState.StartingError"),
            "после ошибки запуска (StartingError) повторный «Старт» обязан начинать новый прогон, а не резюмировать сломанный раннер");
    }

    /// <summary>Тело метода по имени (грубая, но устойчивая к форматированию выборка).</summary>
    private static string MethodBody(string source, string methodName)
    {
        // Берём последнее вхождение: у метода есть определение и место вызова, тело нужно от определения.
        int signature = source.LastIndexOf(methodName + "(", StringComparison.Ordinal);
        Assert.That(signature, Is.GreaterThanOrEqualTo(0), $"не найден метод {methodName}");

        int start = source.LastIndexOf('\n', signature);
        return source.Substring(start, Math.Min(2600, source.Length - start));
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"не найден файл {relativePath}");
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
