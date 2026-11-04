using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using NUnit.Framework;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// `CP-03` — судьба порта `RPA.CodeEditor` (код из OpenRPA).
/// <para>
/// Проект не компилируется: он использует типы <c>OpenRPA.Input</c>/<c>OpenRPA.Interfaces</c>,
/// а сборок OpenRPA в решении нет. Решение: TFM приведён к 4.7.2 (однородность матрицы),
/// проект остаётся <b>вне сборки решения</b> и <b>не подключается к роботам</b>.
/// </para>
/// <para>
/// Раньше этот запрет держался только записью в <c>TfmMatrixGuardTests.KnownProjects</c>, но запись
/// пришлось удалить (TFM изменился). Эти два стража переносят запрет в собственные тесты, чтобы он
/// не исчез вместе с белым списком и не «всплыл» обратно при случае.
/// </para>
/// </summary>
[TestFixture]
public class VsProjectLayoutGuardTests
{
    /// <summary>Имя файла проекта-порта, который нельзя ни собирать в решении, ни подключать к роботам.</summary>
    private const string CodeEditorProjectFile = "RPA.CodeEditor.csproj";

    private static readonly string[] SkippedDirectories =
    {
        "bin", "obj", "packages", ".git", ".vs", "node_modules", "temp", "tmp"
    };

    [Test]
    public void RpaCodeEditor_IsNotReferencedByAnyProject()
    {
        string root = FindRepositoryRoot();
        var offenders = new List<string>();

        foreach (string file in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            if (IsSkipped(file, root))
            {
                continue;
            }

            // Собственный .csproj порта себя, конечно, не подключает — сравниваем по имени файла.
            if (string.Equals(IOPath.GetFileName(file), CodeEditorProjectFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);

            if (text.IndexOf(CodeEditorProjectFile, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                offenders.Add(Relative(root, file));
            }
        }

        Assert.That(offenders, Is.Empty,
            "RPA.CodeEditor — порт OpenRPA, который не компилируется (нет сборок OpenRPA). " +
            "Его нельзя подключать через ProjectReference (CP-03):" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    [Test]
    public void RpaCodeEditor_IsNotBuiltInSolution()
    {
        string root = FindRepositoryRoot();
        string solutionPath = IOPath.Combine(root, "AiRpa.sln");
        Assert.That(File.Exists(solutionPath), Is.True, "не найден AiRpa.sln в корне репозитория");

        string[] lines = File.ReadAllLines(solutionPath);

        var projectLineRegex = new Regex(
            @"^\s*Project\(""\{[^}]+\}""\)\s*=\s*""([^""]+)"",\s*""([^""]+)"",\s*""(\{[^}]+\})""",
            RegexOptions.Compiled);

        string codeEditorGuid = null;
        string projectName = null;

        foreach (string line in lines)
        {
            Match match = projectLineRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            if (match.Groups[2].Value.EndsWith(CodeEditorProjectFile, StringComparison.OrdinalIgnoreCase))
            {
                projectName = match.Groups[1].Value;
                codeEditorGuid = match.Groups[3].Value;
                break;
            }
        }

        Assert.That(codeEditorGuid, Is.Not.Null,
            "в AiRpa.sln не найден проект " + CodeEditorProjectFile + " — если проект удалён, удалите и этот страж");

        var buildEntries = lines
            .Select(l => l.Trim())
            .Where(l => l.StartsWith(codeEditorGuid + ".", StringComparison.OrdinalIgnoreCase))
            .Where(l => l.EndsWith(".Build.0", StringComparison.OrdinalIgnoreCase)
                        || l.IndexOf(".Build.0 ", StringComparison.OrdinalIgnoreCase) > 0)
            .ToList();

        Assert.That(buildEntries, Is.Empty,
            "RPA.CodeEditor (" + projectName + ") должен оставаться вне сборки решения: в AiRpa.sln " +
            "не должно быть строк .Build.0 для " + codeEditorGuid + " (CP-03, TD-12):" + Environment.NewLine +
            string.Join(Environment.NewLine, buildEntries));
    }

    private static string Relative(string root, string file)
    {
        return file.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? file.Substring(root.Length).TrimStart(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar)
            : file;
    }

    private static bool IsSkipped(string file, string root)
    {
        string name = IOPath.GetFileName(file);

        if (name.EndsWith("_wpftmp.csproj", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string relative = Relative(root, file);

        string[] parts = relative.Split(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar);

        // Последний элемент — имя файла, его не проверяем.
        for (int index = 0; index < parts.Length - 1; index++)
        {
            if (SkippedDirectories.Contains(parts[index], StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return parts.Any(part => string.Equals(part, "VSTemplates", StringComparison.OrdinalIgnoreCase));
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
