using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RPA.NuGet.Models;
using RPA.NuGet.Services;

namespace AiRpaStudio.Tests;

/// <summary>
/// Установка транзитивных зависимостей в менеджере NuGet: для <c>packages.config</c>-проектов
/// пакет ставится вместе со своими зависимостями (и их зависимостями), версия подбирается как
/// наименьшая стабильная, удовлетворяющая диапазону. Тесты без сети — источник и nupkg подменены.
/// </summary>
[TestFixture]
public class NuGetTransitiveDependenciesTests
{
    [Test]
    public void ReadDependencies_ReturnsDependenciesOfMatchingFrameworkGroup()
    {
        byte[] nupkg = BuildNupkg("A", "1.0.0", new[]
        {
            new DependencyGroup("net472", new[] { ("B", "1.0.0") }),
            new DependencyGroup("netstandard2.0", new[] { ("C", "2.0.0") })
        });

        IReadOnlyList<NuGetPackageDependency> dependencies = new NuGetPackageDownloader()
            .ReadDependencies(nupkg, "net472");

        Assert.Multiple(() =>
        {
            Assert.That(dependencies.Select(d => d.Id), Is.EqualTo(new[] { "B" }),
                "выбирается группа зависимостей, подходящая целевому фреймворку проекта");
            Assert.That(dependencies[0].VersionRange, Is.EqualTo("[1.0.0, )"),
                "диапазон «1.0.0» нормализуется как [1.0.0, )");
        });
    }

    [Test]
    public void ReadDependencies_UsesAnyGroupWhenNoFrameworkMatches()
    {
        byte[] nupkg = BuildNupkg("A", "1.0.0", new[]
        {
            new DependencyGroup(null, new[] { ("B", "1.0.0") })
        });

        IReadOnlyList<NuGetPackageDependency> dependencies = new NuGetPackageDownloader()
            .ReadDependencies(nupkg, "net472");

        Assert.That(dependencies.Select(d => d.Id), Is.EqualTo(new[] { "B" }),
            "группа без targetFramework применяется к любому проекту");
    }

    [Test]
    public async Task InstallAsync_LegacyProject_InstallsTransitiveDependencies()
    {
        string directory = MakeTempDir();
        try
        {
            string projectPath = Path.Combine(directory, "Legacy.csproj");
            File.WriteAllText(projectPath,
                "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">" +
                "<PropertyGroup><TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(directory, "packages.config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><packages></packages>");

            var packages = new Dictionary<string, byte[]>
            {
                ["A"] = BuildNupkg("A", "1.0.0", new[]
                {
                    new DependencyGroup("net472", new[] { ("B", "2.0.0"), ("C", "[3.0.0,4.0.0)") })
                }),
                ["B"] = BuildNupkg("B", "2.0.0", new[]
                {
                    new DependencyGroup("net472", new[] { ("C", "3.0.0") })
                }),
                ["C"] = BuildNupkg("C", "3.0.0", Array.Empty<DependencyGroup>())
            };
            var versions = new Dictionary<string, string[]>
            {
                ["B"] = new[] { "1.0.0", "2.0.0", "2.5.0" },
                ["C"] = new[] { "3.0.0", "3.1.0", "4.0.0" }
            };

            var search = new TransitiveSearchService(packages, versions);
            var install = new NuGetInstallService(new NuGetPackageDownloader());
            NuGetProjectPackages project = NuGetProjectConfig.Load(projectPath);

            await install.InstallAsync(project, "A", "1.0.0", search);

            Assert.Multiple(() =>
            {
                Assert.That(NuGetProjectConfig.Contains(project, "A"), Is.True);
                Assert.That(NuGetProjectConfig.Contains(project, "B"), Is.True,
                    "прямая зависимость A → B не добавлена в packages.config");
                Assert.That(NuGetProjectConfig.Contains(project, "C"), Is.True,
                    "транзитивная зависимость B → C не добавлена в packages.config");
                Assert.That(NuGetProjectConfig.Find(project, "B").Version, Is.EqualTo("2.0.0"),
                    "для диапазона [2.0.0, ) берётся наименьшая подходящая стабильная версия");
                Assert.That(NuGetProjectConfig.Find(project, "C").Version, Is.EqualTo("3.0.0"),
                    "для диапазона [3.0.0,4.0.0) берётся наименьшая подходящая версия");

                Assert.That(Directory.Exists(Path.Combine(directory, "packages", "A.1.0.0")), Is.True);
                Assert.That(Directory.Exists(Path.Combine(directory, "packages", "B.2.0.0")), Is.True);
                Assert.That(Directory.Exists(Path.Combine(directory, "packages", "C.3.0.0")), Is.True);
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task InstallAsync_SdkProject_AddsOnlyDirectReference()
    {
        string directory = MakeTempDir();
        try
        {
            string projectPath = Path.Combine(directory, "Demo.csproj");
            File.WriteAllText(projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup></Project>");

            var packages = new Dictionary<string, byte[]>
            {
                ["A"] = BuildNupkg("A", "1.0.0", new[]
                {
                    new DependencyGroup("net48", new[] { ("B", "1.0.0") })
                })
            };
            var search = new TransitiveSearchService(packages, new Dictionary<string, string[]> { ["B"] = new[] { "1.0.0" } });
            var install = new NuGetInstallService(new NuGetPackageDownloader());
            NuGetProjectPackages project = NuGetProjectConfig.Load(projectPath);

            await install.InstallAsync(project, "A", "1.0.0", search);

            Assert.Multiple(() =>
            {
                Assert.That(project.Packages.Select(p => p.Id), Is.EqualTo(new[] { "A" }),
                    "PackageReference-проект получает только прямую ссылку; транзитивные зависимости разрешает restore");
                Assert.That(search.Downloads, Is.Empty, "для SDK-проекта nupkg не скачивается вручную");
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task InstallAsync_TransitiveDependency_SkipsPrereleaseWhenRangeIsStable()
    {
        string directory = MakeTempDir();
        try
        {
            string projectPath = Path.Combine(directory, "Legacy.csproj");
            File.WriteAllText(projectPath,
                "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">" +
                "<PropertyGroup><TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(directory, "packages.config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><packages></packages>");

            var packages = new Dictionary<string, byte[]>
            {
                ["A"] = BuildNupkg("A", "1.0.0", new[]
                {
                    new DependencyGroup("net472", new[] { ("B", "1.0.0") })
                }),
                ["B"] = BuildNupkg("B", "1.0.0", Array.Empty<DependencyGroup>())
            };
            var search = new TransitiveSearchService(packages, new Dictionary<string, string[]>
            {
                ["B"] = new[] { "1.0.0-beta", "1.0.0", "1.1.0" }
            });
            var install = new NuGetInstallService(new NuGetPackageDownloader());
            NuGetProjectPackages project = NuGetProjectConfig.Load(projectPath);

            await install.InstallAsync(project, "A", "1.0.0", search);

            Assert.That(NuGetProjectConfig.Find(project, "B").Version, Is.EqualTo("1.0.0"),
                "prerelease не подходит для стабильного диапазона; берётся наименьшая стабильная");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task InstallAsync_CyclicDependencies_DoNotLoop()
    {
        string directory = MakeTempDir();
        try
        {
            string projectPath = Path.Combine(directory, "Legacy.csproj");
            File.WriteAllText(projectPath,
                "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">" +
                "<PropertyGroup><TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(directory, "packages.config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><packages></packages>");

            var packages = new Dictionary<string, byte[]>
            {
                ["A"] = BuildNupkg("A", "1.0.0", new[] { new DependencyGroup("net472", new[] { ("B", "1.0.0") }) }),
                ["B"] = BuildNupkg("B", "1.0.0", new[] { new DependencyGroup("net472", new[] { ("A", "1.0.0") }) })
            };
            var search = new TransitiveSearchService(packages, new Dictionary<string, string[]>
            {
                ["A"] = new[] { "1.0.0" },
                ["B"] = new[] { "1.0.0" }
            });
            var install = new NuGetInstallService(new NuGetPackageDownloader());
            NuGetProjectPackages project = NuGetProjectConfig.Load(projectPath);

            await install.InstallAsync(project, "A", "1.0.0", search);

            Assert.Multiple(() =>
            {
                Assert.That(project.Packages.Select(p => p.Id).OrderBy(id => id), Is.EqualTo(new[] { "A", "B" }),
                    "циклическая зависимость A → B → A не должна дублироваться или зацикливаться");
                Assert.That(search.Downloads.Count, Is.EqualTo(2));
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // ================= Служебное =================

    private sealed class DependencyGroup
    {
        public DependencyGroup(string targetFramework, (string Id, string Version)[] dependencies)
        {
            TargetFramework = targetFramework;
            Dependencies = dependencies;
        }

        public string TargetFramework { get; }

        public (string Id, string Version)[] Dependencies { get; }
    }

    private sealed class TransitiveSearchService : NuGetSearchService
    {
        private readonly Dictionary<string, byte[]> _packages;
        private readonly Dictionary<string, string[]> _versions;

        public TransitiveSearchService(Dictionary<string, byte[]> packages, Dictionary<string, string[]> versions)
            : base("https://local.test/index.json")
        {
            _packages = packages;
            _versions = versions ?? new Dictionary<string, string[]>();
        }

        public List<string> Downloads { get; } = new List<string>();

        public override Task<byte[]> DownloadNupkgAsync(string packageId, string version, CancellationToken cancellationToken = default)
        {
            Downloads.Add(packageId + "|" + version);
            return Task.FromResult(_packages.TryGetValue(packageId, out byte[] nupkg) ? nupkg : null);
        }

        public override Task<IReadOnlyList<string>> GetVersionsAsync(string packageId, bool includePrerelease = false)
        {
            return Task.FromResult<IReadOnlyList<string>>(
                _versions.TryGetValue(packageId, out string[] versions) ? versions : Array.Empty<string>());
        }
    }

    private static byte[] BuildNupkg(string id, string version, DependencyGroup[] groups)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var nuspec = new StringBuilder();
            nuspec.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            nuspec.Append("<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\">");
            nuspec.Append("<metadata><id>").Append(id).Append("</id><version>").Append(version).Append("</version>");
            nuspec.Append("<authors>Test</authors><description>Test package</description>");

            if (groups != null && groups.Length > 0)
            {
                nuspec.Append("<dependencies>");
                foreach (DependencyGroup group in groups)
                {
                    nuspec.Append("<group");
                    if (!string.IsNullOrWhiteSpace(group.TargetFramework))
                        nuspec.Append(" targetFramework=\"").Append(group.TargetFramework).Append("\"");
                    nuspec.Append(">");

                    foreach ((string Id, string Version) dependency in group.Dependencies)
                    {
                        nuspec.Append("<dependency id=\"").Append(dependency.Id)
                            .Append("\" version=\"").Append(dependency.Version).Append("\" />");
                    }

                    nuspec.Append("</group>");
                }

                nuspec.Append("</dependencies>");
            }

            nuspec.Append("</metadata></package>");

            WriteEntry(archive, id + ".nuspec", nuspec.ToString());
        }

        return buffer.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using (Stream stream = entry.Open())
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            writer.Write(content);
        }
    }

    private static string MakeTempDir()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nuget-transitive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
