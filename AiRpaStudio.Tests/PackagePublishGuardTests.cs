using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// Страж публикации NuGet-пакетов активностей.
/// <para>
/// Раньше pack-таргеты трёх библиотек (<c>RpaCommon</c>, <c>ActivityLibraryBase</c>,
/// <c>Activities.Browser</c>) срабатывали на <c>AfterTargets="Build"</c> при конфигурации
/// <c>Release</c> — любая Release-сборка решения инкрементила <c>package.version</c> и парила
/// «мусорный» diff. Правило: пакеты собираются <b>только</b> в конфигурации <c>Publish</c>.
/// </para>
/// <para>
/// Дополнительно держим контракт пакета: у каждого пакета есть <c>README.md</c>, объявленный и в
/// <c>.nuspec</c> (<c>&lt;readme&gt;</c> + <c>&lt;file&gt;</c>), и в проекте (<c>&lt;None&gt;</c>),
/// чтобы файл был виден в Solution Explorer, а не только на диске.
/// </para>
/// </summary>
[TestFixture]
public class PackagePublishGuardTests
{
    private sealed class PackableProject
    {
        public string Csproj { get; set; }
        public string PackTarget { get; set; }
        public string Nuspec { get; set; }
    }

    private static readonly PackableProject[] PackableProjects =
    {
        new PackableProject
        {
            Csproj = @"RpaCommon\RpaCommon.csproj",
            PackTarget = "PackRpaCommon",
            Nuspec = @"RpaCommon\RpaCommon.nuspec"
        },
        new PackableProject
        {
            Csproj = @"ActivityLibraryBase\ActivityLibraryBase.csproj",
            PackTarget = "PackActivityLibraryBase",
            Nuspec = @"ActivityLibraryBase\ActivityLibraryBase.nuspec"
        },
        new PackableProject
        {
            Csproj = @"Activities.Browser\Activities.Browser.csproj",
            PackTarget = "PackActivitiesBrowser",
            Nuspec = @"Activities.Browser\Activities.Browser.nuspec"
        }
    };

    [Test]
    public void PackTargets_RunOnlyInPublishConfiguration()
    {
        var offenders = new List<string>();

        foreach (PackableProject project in PackableProjects)
        {
            string csproj = Full(project.Csproj);
            string text = File.ReadAllText(csproj);

            string targetDeclaration = ExtractTarget(text, project.PackTarget);

            if (targetDeclaration == null)
            {
                offenders.Add(project.Csproj + ": нет таргета " + project.PackTarget);
                continue;
            }

            if (targetDeclaration.IndexOf("'$(Configuration)' == 'Publish'", StringComparison.Ordinal) < 0)
            {
                offenders.Add(project.Csproj + ": таргет " + project.PackTarget
                    + " не привязан к конфигурации Publish");
            }

            if (targetDeclaration.IndexOf("'$(Configuration)' == 'Release'", StringComparison.Ordinal) >= 0)
            {
                offenders.Add(project.Csproj + ": таргет " + project.PackTarget
                    + " всё ещё срабатывает на Release");
            }
        }

        Assert.That(offenders, Is.Empty,
            "Пакеты библиотек активностей должны собираться только в конфигурации Publish, а не Release "
            + "(иначе Release-сборка решения снова будет инкрементить package.version):" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Test]
    public void PublishConfiguration_TargetsPublishOutput()
    {
        var offenders = new List<string>();

        foreach (PackableProject project in PackableProjects)
        {
            string csproj = Full(project.Csproj);
            string text = File.ReadAllText(csproj);

            if (text.IndexOf("'$(Configuration)|$(Platform)' == 'Publish|AnyCPU'", StringComparison.Ordinal) < 0)
            {
                offenders.Add(project.Csproj + ": нет конфигурации Publish|AnyCPU");
                continue;
            }

            if (text.IndexOf("bin\\Publish\\", StringComparison.Ordinal) < 0)
            {
                offenders.Add(project.Csproj + ": конфигурация Publish не кладёт результат в bin\\Publish");
            }
        }

        Assert.That(offenders, Is.Empty,
            "Конфигурация Publish обязана существовать и собирать в bin\\Publish — .nuspec берёт "
            + "библиотеку из bin\\$configuration$:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Test]
    public void Packages_DeclareReadme()
    {
        var offenders = new List<string>();

        foreach (PackableProject project in PackableProjects)
        {
            string nuspec = Full(project.Nuspec);
            string text = File.ReadAllText(nuspec);

            if (text.IndexOf("<readme>README.md</readme>", StringComparison.Ordinal) < 0)
            {
                offenders.Add(project.Nuspec + ": нет <readme>README.md</readme>");
            }

            if (text.IndexOf("<file src=\"README.md\" target=\"\" />", StringComparison.Ordinal) < 0)
            {
                offenders.Add(project.Nuspec + ": нет <file src=\"README.md\" target=\"\" />");
            }
        }

        Assert.That(offenders, Is.Empty,
            "Каждый пакет обязан объявлять README в .nuspec (запись <readme> и включение файла в пакет):"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Test]
    public void ReadmeFiles_ExistAndAreListedInProjects()
    {
        var offenders = new List<string>();

        foreach (PackableProject project in PackableProjects)
        {
            string readme = Full(IOPath.Combine(IOPath.GetDirectoryName(project.Csproj), "README.md"));

            if (!File.Exists(readme))
            {
                offenders.Add(project.Csproj + ": нет файла README.md на диске");
                continue;
            }

            string csproj = Full(project.Csproj);
            string text = File.ReadAllText(csproj);

            if (text.IndexOf("<None Include=\"README.md\" />", StringComparison.Ordinal) < 0)
            {
                offenders.Add(project.Csproj + ": README.md не подключён в проект (<None Include=\"README.md\" />)");
            }
        }

        Assert.That(offenders, Is.Empty,
            "README.md должен существовать и быть подключён в .csproj, иначе файл не виден в Solution Explorer:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Test]
    public void ActivityLibraryBaseNuspec_DeclaresNet472DependencyGroup()
    {
        string nuspec = Full(@"ActivityLibraryBase\ActivityLibraryBase.nuspec");
        string text = File.ReadAllText(nuspec);

        Assert.That(text.IndexOf("<group targetFramework=\"net472\" />", StringComparison.Ordinal) >= 0, Is.True,
            "ActivityLibraryBase.nuspec должен объявлять пустую dependency-группу net472, "
            + "иначе `nuget pack` выдаёт NU5128 (таргет lib/net472 не имеет соответствующей dependency-группы).");
    }

    [Test]
    public void Solution_DebugConfigurationDoesNotMapToPublish()
    {
        string solution = Full("AiRpa.sln");
        string[] lines = File.ReadAllLines(solution);

        // GUID проектов ActivityLibraryBase и Activities.Browser — именно их раньше мапили Debug|Any CPU -> Publish|Any CPU.
        string[] guids = { "{B406DE8E-8CF4-42F3-A234-F7B64B496EB0}", "{0739D690-4CCA-412E-BDF2-56DAB379175D}" };
        var offenders = new List<string>();

        foreach (string guid in guids)
        {
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.StartsWith(guid + ".Debug|Any CPU", StringComparison.OrdinalIgnoreCase)
                    && line.EndsWith("= Publish|Any CPU", StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add(line);
                }
            }
        }

        Assert.That(offenders, Is.Empty,
            "Debug-сборка решения не должна собирать ActivityLibraryBase/Activities.Browser как Publish "
            + "(иначе обычная Debug-сборка начнёт паковать и инкрементить package.version):" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Достаёт открывающий тег <c>&lt;Target Name="..." ...&gt;</c> из .csproj.</summary>
    private static string ExtractTarget(string csproj, string targetName)
    {
        int start = csproj.IndexOf("<Target Name=\"" + targetName + "\"", StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        int end = csproj.IndexOf('>', start);
        return end < 0 ? null : csproj.Substring(start, end - start + 1);
    }

    private static string Full(string relativePath)
    {
        return IOPath.Combine(RepositoryRoot, relativePath);
    }

    private static string RepositoryRoot
    {
        get
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
}
