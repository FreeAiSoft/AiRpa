using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AiRpaStudio.Tests;

/// <summary>
/// Страж переименования сборки ядра VibeCoder.Core -> Agent.Core. Ловит остатки старого имени
/// в исходниках, XAML, csproj и .sln, которые компилятор пропускает молча: именно так
/// рефлексия по строковому имени типа (probe дизайнера) продолжала собираться, но падала в рантайме.
/// </summary>
public class AgentCoreRenameGuardTests
{
    private const string OldAssembly = "VibeCoder.Core";
    private const string OldRoot = "Vibe" + "Coder";

    /// <summary>Файлы, которым старое имя нужно по делу (легаси-фолбэк и сам страж).</summary>
    private static readonly string[] AllowedFiles =
    {
        @"Agent.Core\Services\PromptPaths.cs",
        @"AiRpaStudio.Tests\AgentCoreRenameGuardTests.cs"
    };

    private static readonly string[] Extensions = { ".cs", ".xaml", ".csproj", ".sln" };

    private static readonly string[] SkippedDirectories =
    {
        "bin", "obj", "packages", "tmp", "temp", ".git", ".vs"
    };

    [Test]
    public void SourceArtifacts_DoNotMentionOldAssemblyName()
    {
        string root = FindRepositoryRoot();
        var offenders = new List<string>();

        foreach (string file in SourceFiles(root))
        {
            string relative = RelativePath(root, file);
            if (AllowedFiles.Any(a => string.Equals(a, relative, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            int index = text.IndexOf(OldAssembly, StringComparison.Ordinal);
            if (index < 0)
            {
                index = text.IndexOf(OldRoot, StringComparison.Ordinal);
            }

            if (index >= 0)
            {
                offenders.Add(relative + ":" + LineNumber(text, index));
            }
        }

        Assert.That(offenders, Is.Empty,
            "осталось старое имя сборки (VibeCoder.Core): " + string.Join(", ", offenders));
    }

    [Test]
    public void AllowList_StillPointsAtExistingFiles()
    {
        string root = FindRepositoryRoot();

        Assert.Multiple(() =>
        {
            foreach (string relative in AllowedFiles)
            {
                Assert.That(File.Exists(Path.Combine(root, relative)), Is.True,
                    "allowlist устарел: файла нет - " + relative);
            }
        });
    }

    [Test]
    public void CoreProject_AndSolution_SpeakAgentCore()
    {
        string root = FindRepositoryRoot();
        string csproj = File.ReadAllText(Path.Combine(root, "Agent.Core", "Agent.Core.csproj"));
        string solution = File.ReadAllText(Path.Combine(root, "AiRpa.sln"));

        Assert.Multiple(() =>
        {
            Assert.That(csproj, Does.Contain("<AssemblyName>Agent.Core</AssemblyName>"),
                "имя сборки задано явно: по нему ищут тип probe и VSIX");
            Assert.That(csproj, Does.Contain("<RootNamespace>Agent.Core</RootNamespace>"));
            Assert.That(solution, Does.Contain(@"Agent.Core\Agent.Core.csproj"),
                "решение должно ссылаться на переименованный проект");
        });
    }

    [Test]
    public void Consumers_ReferenceRenamedProject()
    {
        string root = FindRepositoryRoot();
        string[] consumers =
        {
            @"AiRpaStudio\AiRpaStudio.csproj",
            @"RPA.Chat\RPA.Chat.csproj",
            @"AiRpaStudio.Tests\AiRpaStudio.Tests.csproj"
        };

        var missing = consumers
            .Where(c => !File.ReadAllText(Path.Combine(root, c)).Contains(@"Agent.Core\Agent.Core.csproj"))
            .ToList();

        Assert.That(missing, Is.Empty,
            @"потребители обязаны ссылаться на ..\Agent.Core\Agent.Core.csproj: " + string.Join(", ", missing));
    }

    [Test]
    public void ChatControlXaml_ResolvesAgentCoreAssembly()
    {
        string root = FindRepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "RPA.Chat", "Controls", "ChatMessagesControl.xaml"));

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("clr-namespace:Agent.Core.Services;assembly=Agent.Core"),
                "assembly= в XAML компилируется в BAML: имя сборки обязано совпадать");
            Assert.That(xaml, Does.Not.Contain("assembly=Agent.Core.Services"),
                "в assembly= пишется имя сборки, а не пространство имён");
        });
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
                {
                    continue;
                }

                pending.Push(directory);
            }

            foreach (string file in Directory.EnumerateFiles(current))
            {
                if (Extensions.Any(e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return file;
                }
            }
        }
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
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
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
}