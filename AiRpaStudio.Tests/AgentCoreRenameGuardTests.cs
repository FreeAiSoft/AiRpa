using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AiRpaStudio.Tests;

/// <summary>Стражи переименований ядра: VibeCoder.Core -> Agent.Core -> Agent.Rpa -> RPA.Shared.</summary>
public class AgentCoreRenameGuardTests
{
    private const string VibeAssembly = "VibeCoder.Core";
    private const string VibeRoot = "Vibe" + "Coder";
    private const string ForkNamespace = "Agent.Rpa";
    private const string ForkProjectRef = @"Agent.Core\Agent.Core.csproj";

    private static readonly string[] AllowedFiles =
    {
        @"RPA.Shared\Services\PromptPaths.cs",
        @"AiRpaStudio.Tests\AgentCoreRenameGuardTests.cs"
    };

    private static readonly string[] Extensions = { ".cs", ".xaml", ".csproj", ".sln" };

    private static readonly string[] SkippedDirectories =
    {
        "bin", "obj", "packages", "tmp", "temp", ".git", ".vs"
    };

    [Test]
    public void SourceArtifacts_DoNotMentionLegacyNames()
    {
        string root = FindRepositoryRoot();
        var offenders = new List<string>();

        foreach (string file in SourceFiles(root))
        {
            string relative = RelativePath(root, file);
            if (AllowedFiles.Any(a => string.Equals(a, relative, StringComparison.OrdinalIgnoreCase)))
                continue;

            string text = File.ReadAllText(file);
            int index = IndexOfAny(text, VibeAssembly, VibeRoot, ForkNamespace);
            if (index >= 0)
                offenders.Add(relative + ":" + LineNumber(text, index));
        }

        Assert.That(offenders, Is.Empty,
            "осталось старое имя ядра (VibeCoder.Core / Agent.Rpa): " + string.Join(", ", offenders));
    }

    [Test]
    public void AllowList_StillPointsAtExistingFiles()
    {
        string root = FindRepositoryRoot();

        Assert.Multiple(() =>
        {
            foreach (string relative in AllowedFiles)
                Assert.That(File.Exists(Path.Combine(root, relative)), Is.True, "allowlist устарел: " + relative);
        });
    }

    [Test]
    public void SharedProject_AndSolution_SpeakRpaShared()
    {
        string root = FindRepositoryRoot();
        string csproj = File.ReadAllText(Path.Combine(root, "RPA.Shared", "RPA.Shared.csproj"));
        string solution = File.ReadAllText(Path.Combine(root, "AiRpa.sln"));

        Assert.Multiple(() =>
        {
            Assert.That(csproj, Does.Contain("<AssemblyName>RPA.Shared</AssemblyName>"));
            Assert.That(csproj, Does.Contain("<RootNamespace>RPA.Shared</RootNamespace>"));
            Assert.That(solution, Does.Contain(@"RPA.Shared\RPA.Shared.csproj"));
            Assert.That(solution, Does.Not.Contain(ForkProjectRef));
        });
    }

    [Test]
    public void Consumers_ReferenceRpaShared()
    {
        string root = FindRepositoryRoot();
        string[] consumers =
        {
            @"AiRpaStudio\AiRpaStudio.csproj",
            @"RPA.Chat\RPA.Chat.csproj",
            @"AiRpaStudio.Tests\AiRpaStudio.Tests.csproj",
            @"Rpa.RobotPackaging\Rpa.RobotPackaging.csproj"
        };

        var missing = consumers
            .Where(c => !File.ReadAllText(Path.Combine(root, c)).Contains(@"RPA.Shared\RPA.Shared.csproj"))
            .ToList();

        Assert.That(missing, Is.Empty,
            @"потребители обязаны ссылаться на ..\RPA.Shared\RPA.Shared.csproj: " + string.Join(", ", missing));
    }

    private static IEnumerable<string> SourceFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string current = pending.Pop();

            foreach (string directory in Directory.EnumerateDirectories(current))
            {
                string name = Path.GetFileName(directory);
                if (SkippedDirectories.Any(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                pending.Push(directory);
            }

            foreach (string file in Directory.EnumerateFiles(current))
            {
                if (Extensions.Any(e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                    yield return file;
            }
        }
    }

    private static int IndexOfAny(string text, params string[] needles)
    {
        int result = -1;
        foreach (string needle in needles)
        {
            if (string.IsNullOrEmpty(needle)) continue;
            int i = text.IndexOf(needle, StringComparison.Ordinal);
            if (i >= 0 && (result < 0 || i < result)) result = i;
        }

        return result;
    }

    private static string RelativePath(string root, string file)
    {
        return file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static int LineNumber(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n') line++;
        }

        return line;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }
}
