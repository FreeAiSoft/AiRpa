using System;
using System.IO;
using NUnit.Framework;

namespace AiRpaStudio.Tests;

/// <summary>
/// Этап 4: панель «Локальные переменные» очищается вместе со снятием подсветки в ActivityRunner —
/// при старте, продолжении, остановке и завершении прогона.
/// </summary>
public class ActivityRunnerLocalsClearTests
{
    [Test]
    public void Start_ClearsLocalsPanel()
    {
        string method = MethodBody("private void Start(string pathToXaml)");
        Assert.That(method, Does.Contain("ClearLocalsPanel"),
            "старт прогона должен очищать панель Locals");
    }

    [Test]
    public void Resume_ClearsLocalsPanel_BeforeWakingRuntime()
    {
        string method = MethodBody("internal void Run(");

        int clear = method.IndexOf("OnUi(ClearLocalsPanel)", StringComparison.Ordinal);
        int wake = method.IndexOf("_resumeRuntimeFromHost?.Set()", StringComparison.Ordinal);

        Assert.That(clear, Is.GreaterThanOrEqualTo(0), "продолжение прогона должно очищать панель Locals");
        Assert.That(wake, Is.GreaterThanOrEqualTo(0), "возобновление обязано будить фоновый поток");
        Assert.That(clear, Is.LessThan(wake), "панель Locals должна очищаться ДО разблокировки прогона");
    }

    [Test]
    public void OnCompleted_ClearsLocalsPanel()
    {
        string method = MethodBody("void OnCompleted(");
        Assert.That(method, Does.Contain("ClearLocalsPanel"),
            "завершение прогона должно очищать панель Locals");
    }

    [Test]
    public void OnAborted_ClearsLocalsPanel()
    {
        string method = MethodBody("void OnAborted(");
        Assert.That(method, Does.Contain("ClearLocalsPanel"),
            "прерывание прогона должно очищать панель Locals");
    }

    [Test]
    public void WorkflowStop_ClearsLocalsPanel()
    {
        string method = MethodBody("public void WorkflowStop()");
        Assert.That(method, Does.Contain("ClearLocalsPanel"),
            "остановка прогона должна очищать панель Locals");
    }

    [Test]
    public void ClearLocalsPanel_ClearsLocalsModel()
    {
        string method = MethodBody("private static void ClearLocalsPanel()");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("CustomWfDesigner.GetInstance().LocalsModel"),
                "модель Locals берётся не из студии");
            Assert.That(method, Does.Contain(".Clear()"),
                "модель Locals не очищается");
        });
    }

    private static string MethodBody(string signature)
    {
        string source = ReadSource(@"AiRpaStudio\Core\ActivityRunner.cs");
        int index = source.LastIndexOf(signature, StringComparison.Ordinal);
        Assert.That(index, Is.GreaterThanOrEqualTo(0), $"signature not found: {signature}");
        return source.Substring(index, Math.Min(3200, source.Length - index));
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"file not found: {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "repository root not found (AiRpa.sln)");
        return directory.FullName;
    }
}
