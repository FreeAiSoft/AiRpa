using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using NUnit.Framework;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// `CP-08`, шаг «судьба `WorkflowHost`».
/// <para>
/// `WorkflowHost` — прототип запуска робота: зашитый путь к `Main.xaml`, `MessageBox` на ошибке,
/// `Console.ReadKey()`, ссылки на `EnvDTE80`/`Microsoft.VisualStudio.Interop` и своя копия
/// `CustomTrackingParticipant`. Роль раннера у него забирает `OR-02` (`RpaRunner.exe`), а в студии
/// работает `ActivityRunner` + <c>RobotAssemblyResolver</c>. Решение зафиксировано в `AGENTS.md`
/// и `ROADMAP.md`: прототип собирается в составе решения, но <b>никем не подключается</b> —
/// иначе в студии снова появится второй путь запуска со своими правилами.
/// </para>
/// </summary>
[TestFixture]
public class WorkflowHostLegacyGuardTests
{
    private static readonly string[] SkippedDirectories =
    {
        "bin", "obj", "packages", ".git", ".vs", "node_modules", "temp", "tmp"
    };

    [Test]
    public void WorkflowHost_IsNotReferencedByAnyProject()
    {
        string root = FindRepositoryRoot();
        var references = new List<string>();

        foreach (string project in Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            if (IsSkipped(project, root) || IsWorkflowHostItself(project, root))
            {
                continue;
            }

            foreach (string line in File.ReadAllLines(project))
            {
                if (!Regex.IsMatch(line, @"<(ProjectReference|Reference)\b"))
                {
                    continue;
                }

                if (line.IndexOf("WorkflowHost", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    references.Add(Relative(project, root) + ": " + line.Trim());
                }
            }
        }

        Assert.That(references, Is.Empty,
            "прототип `WorkflowHost` никем не подключается: запуск в студии идёт через `ActivityRunner`, "
            + "а раннер — `OR-02`. Появившаяся ссылка вернёт второй путь запуска:"
            + Environment.NewLine + string.Join(Environment.NewLine, references));
    }

    [Test]
    public void WorkflowHost_IsStillBuiltBySolution()
    {
        string solution = File.ReadAllText(IOPath.Combine(FindRepositoryRoot(), "AiRpa.sln"));

        Assert.That(solution, Does.Contain("WorkflowHost\\WorkflowHost.csproj"),
            "прототип остаётся в решении и собирается — иначе он тихо сгниёт, а решение потеряет проверку сборки этого проекта");
    }

    private static bool IsWorkflowHostItself(string project, string root)
    {
        return Relative(project, root).StartsWith("WorkflowHost/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSkipped(string file, string root)
    {
        string[] parts = Relative(file, root).Split('/');

        return parts
            .Take(parts.Length - 1)
            .Any(part => SkippedDirectories.Contains(part, StringComparer.OrdinalIgnoreCase));
    }

    private static string Relative(string path, string root)
    {
        return path
            .Substring(root.Length)
            .TrimStart(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar)
            .Replace(IOPath.DirectorySeparatorChar, '/');
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
