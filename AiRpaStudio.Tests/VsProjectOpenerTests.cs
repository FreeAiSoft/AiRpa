using System;
using System.IO;
using RPA.VsProject;

namespace AiRpaStudio.Tests;

public class VsProjectOpenerTests
{
    [Test]
    public void BuildArguments_QuotesPath()
    {
        Assert.That(
            VsProjectOpener.BuildArguments(@"C:\dir\My Project.csproj"),
            Is.EqualTo("\"C:\\dir\\My Project.csproj\""));
    }

    [Test]
    public void CreateProcessStartInfo_SetsArgumentsAndUsesShell()
    {
        var info = VsProjectOpener.CreateProcessStartInfo(@"C:\dir\x.csproj");

        Assert.That(info.Arguments, Is.EqualTo("\"C:\\dir\\x.csproj\""));
        Assert.That(info.FileName, Is.Not.Null.And.Not.Empty);
        Assert.That(info.UseShellExecute, Is.True);
    }

    [Test]
    public void FindFirstExisting_ReturnsFirstExisting()
    {
        string dir = Path.Combine(Path.GetTempPath(), "vs_opener_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string existing = Path.Combine(dir, "a.txt");
        File.WriteAllText(existing, "x");
        try
        {
            var candidates = new[] { Path.Combine(dir, "missing.txt"), existing };

            Assert.That(VsProjectOpener.FindFirstExisting(candidates), Is.EqualTo(existing));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public void FindFirstExisting_NoExisting_ReturnsNull()
    {
        string dir = Path.Combine(Path.GetTempPath(), "vs_opener_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var candidates = new[] { Path.Combine(dir, "missing1.txt"), Path.Combine(dir, "missing2.txt") };

            Assert.That(VsProjectOpener.FindFirstExisting(candidates), Is.Null);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
