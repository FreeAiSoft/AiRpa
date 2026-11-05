using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using NUnit.Framework;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests.Runner;

/// <summary>
/// `OR-02` — стражи раннера: то, что нельзя проверить прогоном, проверяется чтением исходников.
/// <para>
/// Требование владельца «на чистой машине» держится на трёх запретах: раннер ничего не собирает
/// (нет MSBuild и ссылок на студию), раннер не ждёт человека (нет <c>MessageBox</c> и
/// <c>Console.ReadKey</c>), и движок прогона остаётся один на студию и раннер
/// (нет второй копии <c>WorkflowRunEngine.cs</c>). Каждый запрет здесь закреплён тестом,
/// потому что все три нарушаются «незаметно»: достаточно одной ссылки в csproj.
/// </para>
/// </summary>
[TestFixture]
public class RunnerSourceGuardTests
{
    /// <summary>Проекты и сборки, которых у раннера быть не может: они тянут студию или сборку робота.</summary>
    private static readonly string[] ForbiddenRunnerDependencies =
    {
        "AiRpaStudio", "RPA.Chat", "Agent.Core", "RPA.NuGet", "AvalonDock",
        "Microsoft.Build", "MsBuildLocator", "EnvDTE", "RPA.CodeEditor"
    };

    /// <summary>Признаки UI и ожидания ввода: раннер запускается планировщиком, человека рядом нет.</summary>
    private static readonly string[] ForbiddenTokens =
    {
        "MessageBox", "Console.ReadKey", "Console.ReadLine", "System.Windows", "EnvDTE", "DTE2"
    };

    private static readonly string[] SkippedDirectories =
    {
        "bin", "obj", "packages", ".git", ".vs", "node_modules", "temp", "tmp"
    };

    [Test]
    public void Solution_BuildsRunnerAndItsRuntime()
    {
        string solution = File.ReadAllText(IOPath.Combine(FindRepositoryRoot(), "AiRpa.sln"));

        Assert.Multiple(() =>
        {
            Assert.That(solution, Does.Contain(@"RpaRunner\RpaRunner.csproj"),
                "раннер обязан собираться решением — иначе он тихо сгниёт");
            Assert.That(solution, Does.Contain(@"Rpa.Workflow.Runtime\Rpa.Workflow.Runtime.csproj"),
                "общий рантайм прогона обязан собираться решением");
        });
    }

    [Test]
    public void Runner_ReferencesOnlyTheSharedRuntime()
    {
        string root = FindRepositoryRoot();
        List<string> references = ProjectReferences(IOPath.Combine(root, "RpaRunner", "RpaRunner.csproj"));

        Assert.Multiple(() =>
        {
            Assert.That(references, Is.Not.Empty, "раннер обязан ссылаться на общий рантайм прогона");
            Assert.That(references.Count(r => r.IndexOf("Rpa.Workflow.Runtime", StringComparison.OrdinalIgnoreCase) >= 0),
                Is.EqualTo(1), "общий рантайм обязан быть подключён ровно одной ссылкой");

            foreach (string dependency in ForbiddenRunnerDependencies)
            {
                Assert.That(references.Any(r => r.IndexOf(dependency, StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.False,
                    $"раннер не имеет права ссылаться на «{dependency}»: он ставится на чистую машину рядом с роботом");
            }
        });
    }

    [Test]
    public void Runtime_ReferencesNoStudioProject()
    {
        string root = FindRepositoryRoot();
        List<string> references = ProjectReferences(IOPath.Combine(root, "Rpa.Workflow.Runtime", "Rpa.Workflow.Runtime.csproj"));

        Assert.Multiple(() =>
        {
            Assert.That(references.Any(r => r.IndexOf("ActivitiesTracker", StringComparison.OrdinalIgnoreCase) >= 0),
                Is.True, "рантайму нужен трекер активностей робота");

            foreach (string forbidden in new[] { "AiRpaStudio", "RPA.Chat", "Agent.Core", "Microsoft.Build", "MsBuildLocator" })
            {
                Assert.That(references.Any(r => r.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.False,
                    $"общий рантайм прогона не имеет права тянуть «{forbidden}»: он уезжает на чистую машину");
            }
        });
    }

    [Test]
    public void Runner_TargetsNet472()
    {
        string csproj = File.ReadAllText(IOPath.Combine(FindRepositoryRoot(), "RpaRunner", "RpaRunner.csproj"));

        Assert.That(csproj, Does.Contain("<TargetFramework>net472</TargetFramework>"),
            "раннер обязан целиться в 4.7.2 (CP-10): он ставится рядом с роботом, а не со студией");
    }

    [Test]
    public void RunnerSources_HaveNoUiOrBlockingInput()
    {
        string root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (string file in Sources(IOPath.Combine(root, "RpaRunner")))
        {
            string text = CodeOnly(File.ReadAllText(file));

            foreach (string token in ForbiddenTokens)
            {
                if (text.IndexOf(token, StringComparison.Ordinal) >= 0)
                {
                    violations.Add(IOPath.GetFileName(file) + ": " + token);
                }
            }
        }

        Assert.That(violations, Is.Empty,
            "раннер запускается планировщиком и оркестратором — ни UI, ни ожидания ввода:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Test]
    public void RuntimeSources_HaveNoUi()
    {
        string root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (string file in Sources(IOPath.Combine(root, "Rpa.Workflow.Runtime")))
        {
            string text = CodeOnly(File.ReadAllText(file));

            foreach (string token in ForbiddenTokens)
            {
                if (text.IndexOf(token, StringComparison.Ordinal) >= 0)
                {
                    violations.Add(IOPath.GetFileName(file) + ": " + token);
                }
            }
        }

        Assert.That(violations, Is.Empty,
            "общий рантайм прогона обязан оставаться headless (иначе прогон в раннере потребует UI):" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Test]
    public void ThereIsExactlyOneRunEngineSource()
    {
        string root = FindRepositoryRoot();
        string[] engines = Directory
            .EnumerateFiles(root, "WorkflowRunEngine.cs", SearchOption.AllDirectories)
            .Where(file => !IsSkipped(file, root))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(engines, Has.Length.EqualTo(1),
                "движок прогона обязан быть один на студию и раннер: " + string.Join("; ", engines));
            Assert.That(engines[0], Does.Contain(IOPath.Combine("Rpa.Workflow.Runtime", "Engine")));
        });
    }

    [Test]
    public void Studio_UsesTheSharedRuntimeInsteadOfItsOwnCopy()
    {
        string studio = File.ReadAllText(IOPath.Combine(FindRepositoryRoot(), "AiRpaStudio", "AiRpaStudio.csproj"));

        Assert.Multiple(() =>
        {
            Assert.That(studio, Does.Contain(@"Rpa.Workflow.Runtime\Rpa.Workflow.Runtime.csproj"),
                "студия обязана брать движок из общего рантайма");
            Assert.That(studio, Does.Not.Contain(@"Core\WorkflowRunEngine.cs"),
                "вторая копия движка в студии разошлась бы с раннером");
        });
    }

    [Test]
    public void Solution_BuildsPackagingProjects()
    {
        string solution = File.ReadAllText(IOPath.Combine(FindRepositoryRoot(), "AiRpa.sln"));

        Assert.Multiple(() =>
        {
            Assert.That(solution, Does.Contain(@"Rpa.Orchestrator.Contracts\Rpa.Orchestrator.Contracts.csproj"),
                "контракты пакета обязаны собираться решением — их читает и раннер, и сервер");
            Assert.That(solution, Does.Contain(@"Rpa.RobotPackaging\Rpa.RobotPackaging.csproj"),
                "упаковщик обязан собираться решением");
            Assert.That(solution, Does.Contain(@"RpaPack\RpaPack.csproj"),
                "инструмент упаковки обязан собираться решением: им собирают пакеты");
        });
    }

    [Test]
    public void RunnerAndRuntime_DoNotReferencePackaging()
    {
        string root = FindRepositoryRoot();

        Assert.Multiple(() =>
        {
            foreach (string project in new[] { "RpaRunner", "Rpa.Workflow.Runtime" })
            {
                List<string> references = ProjectReferences(IOPath.Combine(root, project, project + ".csproj"));

                Assert.That(references.Any(r => r.IndexOf("Rpa.RobotPackaging", StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.False,
                    $"«{project}» не имеет права ссылаться на упаковщик: упаковка тянет MSBuild, а прогон — нет");
            }
        });
    }

    [Test]
    public void Packaging_ReferencesNoStudioOrUi()
    {
        string root = FindRepositoryRoot();
        List<string> references = ProjectReferences(
            IOPath.Combine(root, "Rpa.RobotPackaging", "Rpa.RobotPackaging.csproj"));

        Assert.Multiple(() =>
        {
            Assert.That(references.Any(r => r.IndexOf("Rpa.Orchestrator.Contracts", StringComparison.OrdinalIgnoreCase) >= 0),
                Is.True, "упаковщик обязан пользоваться общим форматом пакета, а не своим");

            foreach (string forbidden in new[] { "AiRpaStudio", "RPA.Chat", "RPA.CodeEditor", "AvalonDock" })
            {
                Assert.That(references.Any(r => r.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.False,
                    $"упаковщик не имеет права тянуть «{forbidden}»: он работает на агенте и в скрипте сборки");
            }
        });
    }

    [Test]
    public void ContractsHaveNoDependencies()
    {
        string root = FindRepositoryRoot();
        string csproj = File.ReadAllText(
            IOPath.Combine(root, "Rpa.Orchestrator.Contracts", "Rpa.Orchestrator.Contracts.csproj"));

        Assert.Multiple(() =>
        {
            Assert.That(ProjectReferences(IOPath.Combine(root, "Rpa.Orchestrator.Contracts", "Rpa.Orchestrator.Contracts.csproj")),
                Is.Empty,
                "контракты пакета обязаны обходиться одной BCL: иначе формат перестанет читаться на 4.7.2 и на сервере одинаково");
            Assert.That(csproj, Does.Contain("<TargetFramework>netstandard2.0</TargetFramework>"),
                "контракты обязаны целиться в netstandard2.0 (ROADMAP §8.3)");
        });
    }

    /// <summary>
    /// Оставляет только код: стражи ищут вызовы, а не упоминания.
    /// <para>
    /// В документации раннера прямо написано «ни <c>MessageBox</c>, ни <c>Console.ReadKey()</c>»,
    /// а в <c>ErrorText</c> приведён пример текста ошибки с именем WPF-типа — иначе запрет не
    /// объяснить. Без этой очистки страж падал бы на собственных пояснениях, то есть требовал бы
    /// молчать о запрете вместо того, чтобы его соблюдать. Строковые литералы сохраняются:
    /// <c>MessageBox</c> в строке — это уже не комментарий.
    /// </para>
    /// </summary>
    private static string CodeOnly(string text)
    {
        var code = new StringBuilder(text.Length);
        bool inString = false;
        bool inChar = false;
        bool inVerbatim = false;
        bool inLineComment = false;
        bool inBlockComment = false;

        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            char next = index + 1 < text.Length ? text[index + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\n')
                {
                    inLineComment = false;
                    code.Append(current);
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    index++;
                }

                continue;
            }

            if (inString)
            {
                code.Append(current);

                if (inVerbatim)
                {
                    if (current == '"')
                    {
                        if (next == '"')
                        {
                            code.Append(next);
                            index++;
                        }
                        else
                        {
                            inString = false;
                        }
                    }
                }
                else if (current == '\\' && next != '\0')
                {
                    code.Append(next);
                    index++;
                }
                else if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (inChar)
            {
                code.Append(current);

                if (current == '\\' && next != '\0')
                {
                    code.Append(next);
                    index++;
                }
                else if (current == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            if (current == '/' && next == '/')
            {
                inLineComment = true;
                index++;
                continue;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                index++;
                continue;
            }

            if (current == '@' && next == '"')
            {
                inString = true;
                inVerbatim = true;
                code.Append(current).Append(next);
                index++;
                continue;
            }

            if (current == '"')
            {
                inString = true;
                inVerbatim = false;
            }
            else if (current == '\'')
            {
                inChar = true;
            }

            code.Append(current);
        }

        return code.ToString();
    }

    private static IEnumerable<string> Sources(string directory)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            string[] parts = file.Split(IOPath.DirectorySeparatorChar);

            if (parts.Any(part => SkippedDirectories.Contains(part, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            yield return file;
        }
    }

    private static List<string> ProjectReferences(string csprojPath)
    {
        var references = new List<string>();

        foreach (string line in File.ReadAllLines(csprojPath))
        {
            Match match = Regex.Match(line, @"<ProjectReference\s+Include=""([^""]+)""|<PackageReference\s+Include=""([^""]+)""|<Reference\s+Include=""([^""]+)""");

            if (!match.Success)
            {
                continue;
            }

            references.Add(match.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value);
        }

        return references;
    }

    private static bool IsSkipped(string file, string root)
    {
        string relative = file.Substring(root.Length).TrimStart(IOPath.DirectorySeparatorChar);
        string[] parts = relative.Split(IOPath.DirectorySeparatorChar);

        return parts.Take(Math.Max(0, parts.Length - 1))
            .Any(part => SkippedDirectories.Contains(part, StringComparer.OrdinalIgnoreCase));
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
