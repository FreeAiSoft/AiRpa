using System;
using System.IO;
using NUnit.Framework;

namespace AiRpaStudio.Tests;

/// <summary>
/// Regression guard for the bug where the yellow frame (CurrentLocation) stayed on
/// nested XAML tabs after the workflow finished, because only the root designer's
/// highlight was cleared.
/// </summary>
public class ActivityRunnerClearHighlightTests
{
    [Test]
    public void ClearCurrentLocationOnAllDesigners_ClearsEveryOpenDesigner()
    {
        string method = MethodBody(
            ReadSource(@"AiRpaStudio\Core\ActivityRunner.cs"),
            "ClearCurrentLocationOnAllDesigners");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("WorkflowDesigners"),
                "the reset must iterate over all open designers");
            Assert.That(method, Does.Contain("DebugManagerView.CurrentLocation = null"),
                "every designer must have its current-activity highlight cleared");
        });
    }

    [Test]
    public void OnCompleted_ClearsHighlightOnAllDesigners()
    {
        string method = MethodBody(
            ReadSource(@"AiRpaStudio\Core\ActivityRunner.cs"),
            "OnCompleted");

        Assert.That(method, Does.Contain("ClearCurrentLocationOnAllDesigners()"),
            "completion must clear the highlight on all designers");
    }

    [Test]
    public void OnAborted_ClearsHighlightOnAllDesigners()
    {
        string method = MethodBody(
            ReadSource(@"AiRpaStudio\Core\ActivityRunner.cs"),
            "OnAborted");

        Assert.That(method, Does.Contain("ClearCurrentLocationOnAllDesigners()"),
            "aborting must clear the highlight on all designers");
    }

    [Test]
    public void RunNoLongerClearsOnlyRootDesigner()
    {
        string source = ReadSource(@"AiRpaStudio\Core\ActivityRunner.cs");

        Assert.That(source, Does.Not.Contain("_workflowDesignerEx.DebugManagerView.CurrentLocation = null"),
            "the old root-only reset must be gone: nested tabs kept the frame after the run");
    }

    private static string MethodBody(string source, string methodName)
    {
        int signature = source.LastIndexOf(methodName + "(", StringComparison.Ordinal);
        Assert.That(signature, Is.GreaterThanOrEqualTo(0), $"method not found: {methodName}");

        int start = source.LastIndexOf('\n', signature);
        return source.Substring(start, Math.Min(3200, source.Length - start));
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
