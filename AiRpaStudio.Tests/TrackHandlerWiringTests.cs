using System;
using System.IO;
using NUnit.Framework;

namespace AiRpaStudio.Tests;

/// <summary>
/// Ф2 — стражи отладки во вложенных скриптах. Здесь проверяется то, что трудно проверить юнит-тестом
/// (нужен живой <c>CustomWfDesigner</c> + AvalonDock + dispatcher): при попадании в точку останова
/// вкладка целевого дизайнера обязана активироваться, а не только подсвечиваться в фоне.
/// </summary>
public class TrackHandlerWiringTests
{
    [Test]
    public void ShowDebug_ActivatesTargetDesignerOnBreakpointHit()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Helpers\TrackHandler.cs"), "ShowDebug");

        int hitIndex = method.IndexOf("if (isBreakpointHit)", StringComparison.Ordinal);
        Assert.That(hitIndex, Is.GreaterThanOrEqualTo(0), "не найден блок обработки попадания в точку останова");

        string hitBlock = method.Substring(hitIndex);
        Assert.That(hitBlock, Does.Contain("ActiveWorkflowDesignerEx = targetDesigner"),
            "при попадании в точку останова вкладка целевого дизайнера должна активироваться");
    }

    /// <summary>Тело метода по имени (грубая, но устойчивая к форматированию выборка).</summary>
    private static string MethodBody(string source, string methodName)
    {
        int signature = source.LastIndexOf(methodName + "(", StringComparison.Ordinal);
        Assert.That(signature, Is.GreaterThanOrEqualTo(0), $"не найден метод {methodName}");

        int start = source.LastIndexOf('\n', signature);
        return source.Substring(start, Math.Min(3200, source.Length - start));
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