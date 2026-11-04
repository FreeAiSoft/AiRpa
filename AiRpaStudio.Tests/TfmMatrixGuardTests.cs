using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

using NUnit.Framework;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// `CP-01` — матрица целевых платформ (TFM) как страж.
/// <para>
/// Смысл: студия работает на 4.8, а роботы и всё, что они тянут за собой, обязаны
/// собираться на <b>.NET Framework 4.7.2</b>. Поэтому любой проект решения должен либо
/// целиться в TFM, совместимый с 4.7.2, либо быть явно внесён в <see cref="KnownProjects"/>
/// с ID задачи роадмэпа и причиной. Новый «несовместимый» проект без записи в белом списке
/// валит тест; исправленный проект требует удаления записи (тест <see cref="KnownProjects_ExistAndMatchRecordedTfm"/>),
/// чтобы белый список не протухал.
/// </para>
/// <para>
/// Сканируются все <c>*.csproj</c> под корнем репозитория, кроме <c>bin/obj/packages/temp/tmp</c>,
/// WPF-временных <c>*_wpftmp.csproj</c> и VS-шаблонов (<c>VSTemplates</c>, плейсхолдер
/// <c>$targetframeworkversion$</c> — там TFM подставляется при создании проекта).
/// </para>
/// </summary>
[TestFixture]
public class TfmMatrixGuardTests
{
    /// <summary>TFM, с которыми может сосуществовать проект-робот на .NET Framework 4.7.2.</summary>
    private static readonly string[] CompatibleWithRobot =
    {
        "net462",        // ветка ICSharpCode.AvalonEdit, которую использует 4.7.2
        "net472",        // эталон роботов
        "netstandard2.0" // Agent.Core и общие контракты (пойдёт и в .NET 8/9)
    };

    /// <summary>Каталоги, содержимое которых не является исходниками решения.</summary>
    private static readonly string[] SkippedDirectories =
    {
        "bin", "obj", "packages", ".git", ".vs", "node_modules", "temp", "tmp"
    };

    /// <summary>Проекты, которые заведомо нельзя подключать к роботам: причина и ID задачи.</summary>
    private static readonly KnownProject[] KnownProjects =
    {
        new KnownProject("AiRpaStudio", "net48", "хост",
            "студия-хост на 4.8; роботы на неё не ссылаются"),
        new KnownProject("AiRpaStudio.Tests", "net48", "хост",
            "тесты студии, в поставку робота не входят"),
        new KnownProject("RPA.Chat", "net48", "хост",
            "сборка студии (чат/агент), подключается только студией"),
        new KnownProject("RPA.NuGet", "net48", "хост",
            "панель NuGet внутри студии"),
        new KnownProject("Extensions", "net5.0", "CP-05",
            "утилиты цепочки IpApiClient/ProxyChecker; роботы не ссылаются"),
        new KnownProject("Shared", "net5.0", "CP-05",
            "общие модели цепочки IpApiClient/ProxyChecker; роботы не ссылаются"),
        new KnownProject("IpApiClient", "net6.0", "CP-05",
            "клиент IP-API для ProxyChecker; вне 4.7.2-рантайма"),
        new KnownProject("ProxyChecker", "net6.0-windows", "CP-05",
            "SDK-style net6.0-windows, вне 4.7.2-рантайма; сборка починена 03.11.2026 по TD-12: добавлена ссылка на EasyMvvm"),
        new KnownProject("EasyMvvm", "net6.0-windows", "CP-05",
            "отдельный от FW.EasyMvvm проект (net6), в решение не входит; используется ProxyChecker"),
        new KnownProject("Net48Robot", "net48", "CP-09",
            "эталонный робот SDK-style на net48 (AiRpaStudio\\TestProcess\\Net48Robot): проверяет второй формат проекта и restore, в поставку не входит; пара к Net472Robot")

    };

    private static readonly string[] KeyRobotProjects =
    {
        "Activities", "Activities.Browser", "Activities.Debug", "Activities.Files",
        "Activities.Flow", "Activities.OS", "ActivitiesTracker", "ActivityCodeTemplate",
        "ActivityLibraryBase", "DesignerTemplate", "FW.EasyMvvm", "LogControlLibrary",
        "RPA-1", "Rpa.Styles", "RpaCommon", "WorkflowHost"
    };

    [Test]
    public void EveryProject_IsCompatibleWithNet472_OrListedInKnownProjects()
    {
        List<ProjectTfms> projects = ScanProjects(out List<string> scanErrors);

        Assert.That(scanErrors, Is.Empty, "не удалось разобрать .csproj: " + string.Join("; ", scanErrors));

        var violations = new List<string>();

        foreach (ProjectTfms project in projects)
        {
            if (project.Tfms.Any(IsCompatibleWithRobot))
            {
                continue;
            }

            if (project.Tfms.Count == 0)
            {
                violations.Add(project.Name + ": не найден TFM (ни TargetFramework, ни TargetFrameworkVersion)");
                continue;
            }

            KnownProject known = FindKnown(project.Name);
            string actual = project.Tfms[0];

            if (known == null)
            {
                violations.Add(string.Format(
                    "{0} ({1}): несовместим с 4.7.2 и нет записи в KnownProjects — либо приведите проект к net472/netstandard2.0, либо внесите запись с ID задачи и причиной",
                    project.Name, string.Join(";", project.Tfms)));
            }
            else if (!string.Equals(known.Tfm, actual, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(string.Format(
                    "{0}: в KnownProjects записан TFM {1}, а в проекте {2} — обновите запись ({3})",
                    project.Name, known.Tfm, string.Join(";", project.Tfms), known.RoadmapId));
            }
        }

        Assert.That(violations, Is.Empty,
            "нарушения матрицы TFM (цель Ц2 — совместимость с .NET Framework 4.7.2):" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Test]
    public void KnownProjects_ExistAndMatchRecordedTfm()
    {
        List<ProjectTfms> projects = ScanProjects(out _);

        var problems = new List<string>();

        foreach (KnownProject known in KnownProjects)
        {
            ProjectTfms project = projects.FirstOrDefault(p =>
                string.Equals(p.Name, known.Name, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                problems.Add(known.Name + ": запись в KnownProjects есть, а проекта в репозитории нет — удалите запись");
                continue;
            }

            if (!project.Tfms.Any(t => string.Equals(t, known.Tfm, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add(string.Format(
                    "{0}: запись «{1}» ({2}) больше не соответствует проекту ({3}) — если задача {2} закрыта, удалите запись",
                    known.Name, known.Tfm, known.RoadmapId, string.Join(";", project.Tfms)));
            }
        }

        Assert.That(problems, Is.Empty,
            "белый список матрицы TFM протух:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    [Test]
    public void Scan_FindsKeyProjects()
    {
        List<ProjectTfms> projects = ScanProjects(out _);
        var names = new HashSet<string>(projects.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        var missing = KeyRobotProjects.Where(name => !names.Contains(name)).ToList();

        Assert.That(missing, Is.Empty,
            "скан не нашёл проекты роботов — страж матрицы TFM бесполезен: " + string.Join(", ", missing));
    }

    [Test]
    public void RobotProjects_TargetExactlyNet472()
    {
        List<ProjectTfms> projects = ScanProjects(out _);

        var wrong = new List<string>();

        foreach (string name in KeyRobotProjects)
        {
            ProjectTfms project = projects.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                wrong.Add(name + ": проект не найден");
                continue;
            }

            if (!project.Tfms.Contains("net472"))
            {
                wrong.Add(name + ": TFM " + string.Join(";", project.Tfms) + " вместо net472");
            }
        }

        Assert.That(wrong, Is.Empty,
            "эталонные проекты роботов должны целиться в net472:" + Environment.NewLine + string.Join(Environment.NewLine, wrong));
    }

    private static bool IsCompatibleWithRobot(string tfm)
    {
        return CompatibleWithRobot.Contains(tfm, StringComparer.OrdinalIgnoreCase);
    }

    private static KnownProject FindKnown(string name)
    {
        return KnownProjects.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Читает все <c>*.csproj</c> репозитория и возвращает их нормализованные TFM.</summary>
    private static List<ProjectTfms> ScanProjects(out List<string> errors)
    {
        errors = new List<string>();
        string root = FindRepositoryRoot();
        var result = new List<ProjectTfms>();

        IEnumerable<string> files = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories);

        foreach (string file in files)
        {
            if (IsSkipped(file, root))
            {
                continue;
            }

            try
            {
                XDocument document = XDocument.Load(file);

                string placeholder = document.Descendants()
                    .Where(e => e.Name.LocalName == "TargetFrameworkVersion" || e.Name.LocalName == "TargetFramework")
                    .Select(e => e.Value)
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v) && v.Contains("$"));

                if (placeholder != null)
                {
                    // VS-шаблон: значение подставляется при создании проекта.
                    continue;
                }

                List<string> tfms = ReadTfms(document);

                result.Add(new ProjectTfms
                {
                    Name = IOPath.GetFileNameWithoutExtension(file),
                    Path = file,
                    Tfms = tfms
                });
            }
            catch (Exception exception)
            {
                errors.Add(IOPath.GetFileName(file) + ": " + exception.Message);
            }
        }

        return result;
    }

    private static List<string> ReadTfms(XDocument document)
    {
        var tfms = new List<string>();

        foreach (XElement element in document.Descendants())
        {
            if (element.Name.LocalName == "TargetFrameworks" || element.Name.LocalName == "TargetFramework")
            {
                foreach (string value in element.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    AddNormalized(tfms, value);
                }
            }
            else if (element.Name.LocalName == "TargetFrameworkVersion")
            {
                AddNormalized(tfms, element.Value);
            }
        }

        return tfms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddNormalized(List<string> tfms, string raw)
    {
        string value = (raw ?? string.Empty).Trim();

        if (value.Length == 0)
        {
            return;
        }

        // Старый формат: v4.7.2 -> net472, v4.8 -> net48, v4.8.1 -> net481.
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring(1);
        }

        if (value.All(c => char.IsDigit(c) || c == '.'))
        {
            tfms.Add(value.StartsWith("4.", StringComparison.Ordinal) ? "net" + value.Replace(".", string.Empty) : "net" + value);
        }
        else
        {
            tfms.Add(value.ToLowerInvariant());
        }
    }

    private static bool IsSkipped(string file, string root)
    {
        string name = IOPath.GetFileName(file);

        if (name.EndsWith("_wpftmp.csproj", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string relative = file.Substring(root.Length).TrimStart(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar);

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

    private sealed class ProjectTfms
    {
        public string Name { get; set; }

        public string Path { get; set; }

        public List<string> Tfms { get; set; }
    }

    private sealed class KnownProject
    {
        public KnownProject(string name, string tfm, string roadmapId, string reason)
        {
            Name = name;
            Tfm = tfm;
            RoadmapId = roadmapId;
            Reason = reason;
        }

        public string Name { get; }

        public string Tfm { get; }

        public string RoadmapId { get; }

        public string Reason { get; }
    }
}
