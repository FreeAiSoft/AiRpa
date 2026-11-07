using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using RPA.NuGet.Models;
using RPA.NuGet.Services;
using RPA.NuGet.ViewModels;

namespace AiRpaStudio.Tests;

/// <summary>
/// Вкладка README панели «NuGet»: она показывается только когда README реально прочитан из пакета,
/// поэтому проверяется вся цепочка — поиск файла внутри <c>.nupkg</c>, скачивание из выбранного
/// источника, запасная версия, кеш и появление вкладки в разметке.
/// <para>
/// Каждый тест берёт собственный идентификатор пакета: прочитанные README кешируются статически
/// (<c>id|version</c>), и общий идентификатор сделал бы результат зависимым от порядка тестов.
/// </para>
/// </summary>
[TestFixture]
public class NuGetReadmeLoadTests
{
    [Test]
    public void ReadReadme_ReadsRootReadme()
    {
        byte[] nupkg = BuildPackage(new Dictionary<string, string>
        {
            ["README.md"] = "# Заголовок\n\nТекст пакета."
        });

        Assert.That(NuGetPackageDetailsService.ReadReadme(nupkg), Does.Contain("Заголовок"));
    }

    [Test]
    public void ReadReadme_ReadsReadmeFromSubfolder()
    {
        // README в docs\ — у многих пакетов он лежит не в корне: раньше такие пакеты
        // выглядели как «README нет», и вкладка исчезала.
        byte[] nupkg = BuildPackage(new Dictionary<string, string>
        {
            ["docs/README.md"] = "# Документация\n\nОписание."
        });

        Assert.That(NuGetPackageDetailsService.ReadReadme(nupkg), Does.Contain("Документация"));
    }

    [Test]
    public void ReadReadme_UsesNuspecDeclaredEntry()
    {
        byte[] nupkg = BuildPackage(
            new Dictionary<string, string>
            {
                ["readme/overview.txt"] = "Обзор пакета",
                ["README.md"] = "# Другой файл"
            },
            readmeEntry: "readme/overview.txt");

        Assert.That(NuGetPackageDetailsService.ReadReadme(nupkg), Is.EqualTo("Обзор пакета"),
            "объявленный в nuspec README главнее файла с похожим именем");
    }

    [Test]
    public void ReadReadme_ReturnsNullWhenPackageHasNoReadme()
    {
        byte[] nupkg = BuildPackage(new Dictionary<string, string>
        {
            ["lib/net472/Sample.dll"] = "not a readme"
        });

        Assert.Multiple(() =>
        {
            Assert.That(NuGetPackageDetailsService.ReadReadme(nupkg), Is.Null);
            Assert.That(NuGetPackageDetailsService.ReadReadme(null), Is.Null);
            Assert.That(NuGetPackageDetailsService.ReadReadme(Array.Empty<byte>()), Is.Null);
        });
    }

    [Test]
    public async Task TryReadReadme_DownloadsFromSelectedSourceAndCaches()
    {
        var source = new FakeSource();
        source.Add("Cache.Package", "1.0.0", BuildPackage(new Dictionary<string, string>
        {
            ["README.md"] = "# Из источника"
        }));

        var service = new NuGetPackageDetailsService(new ThrowingDownloader());
        string readme = await service.TryReadReadmeAsync(source, "Cache.Package", "1.0.0");

        Assert.Multiple(() =>
        {
            Assert.That(readme, Does.Contain("Из источника"), "README берётся из выбранного источника");
            Assert.That(service.LastReadmeError, Is.Null, "ошибки нет — README прочитан");
            Assert.That(source.Downloads, Has.Count.EqualTo(1), "пакет скачивается ровно один раз");
        });

        // Повторный выбор пакета не ходит в сеть: ответ берётся из кеша.
        string again = await service.TryReadReadmeAsync(source, "Cache.Package", "1.0.0");
        Assert.Multiple(() =>
        {
            Assert.That(again, Does.Contain("Из источника"));
            Assert.That(source.Downloads, Has.Count.EqualTo(1), "второй запрос к источнику не нужен");
        });
    }

    [Test]
    public async Task TryReadReadme_FallsBackToAnotherVersion()
    {
        // Версии, показанной в панели, у источника уже нет (в проекте зафиксирована удалённая версия).
        var source = new FakeSource();
        source.Add("Fallback.Package", "2.0.0", BuildPackage(new Dictionary<string, string>
        {
            ["README.md"] = "# Последняя версия"
        }));

        var service = new NuGetPackageDetailsService(new ThrowingDownloader());
        string readme = await service.TryReadReadmeAsync(source, "Fallback.Package", "0.9.0-removed", "2.0.0");

        Assert.Multiple(() =>
        {
            Assert.That(readme, Does.Contain("Последняя версия"), "README берётся у запасной версии");
            Assert.That(source.Downloads, Does.Contain("Fallback.Package|0.9.0-removed"));
            Assert.That(source.Downloads, Does.Contain("Fallback.Package|2.0.0"));
        });
    }

    [Test]
    public async Task TryReadReadme_ReportsFailureReasonInsteadOfSilence()
    {
        var source = new FakeSource();
        var service = new NuGetPackageDetailsService(new ThrowingDownloader());

        string readme = await service.TryReadReadmeAsync(source, "Missing.Package", "1.0.0");

        Assert.Multiple(() =>
        {
            Assert.That(readme, Is.Null);
            Assert.That(service.LastReadmeError, Is.Not.Null.And.Not.Empty,
                "причина обязана быть видна: иначе пропавшая вкладка README выглядит как «в пакете нет README»");
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Panel_ReadmeTabAppearsWhenReadmeLoaded()
    {
        var info = new NuGetPackageInfo
        {
            Id = "Panel.Package",
            Version = "9.9.9",
            Authors = "Test",
            Description = "Пакет с README"
        };

        var source = new FakeSource();
        source.Packages[info.Id] = info;
        source.Add(info.Id, info.Version, BuildPackage(new Dictionary<string, string>
        {
            ["README.md"] = "# Заголовок\n\nТекст."
        }));

        var model = new NuGetPanelViewModel(source, new ThrowingDownloader(), new NuGetIconService(new HttpClient()));
        var panel = new RPA.NuGet.Views.NuGetPanel(model);

        model.BrowseItems.Add(new NuGetPackageItemViewModel(info));
        model.SelectedItem = model.BrowseItems[0];

        panel.Measure(new Size(1100, 700));
        panel.Arrange(new Rect(0, 0, 1100, 700));
        panel.UpdateLayout();

        var readmeTab = panel.FindName("ReadmeTabButton") as RadioButton;

        Assert.Multiple(() =>
        {
            Assert.That(model.Details.ShowReadmeTab, Is.True, "README получен — вкладка обязана появиться");
            Assert.That(model.Details.Readme, Does.Contain("Заголовок"));
            Assert.That(readmeTab, Is.Not.Null, "в разметке нет вкладки README");
            Assert.That(readmeTab.Visibility, Is.EqualTo(Visibility.Visible), "вкладка README скрыта при готовом README");
        });
    }

    // ================= Служебное =================

    /// <summary>Собирает минимальный валидный <c>.nupkg</c> в памяти.</summary>
    private static byte[] BuildPackage(
        IDictionary<string, string> files,
        string readmeEntry = null)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            string readmeElement = string.IsNullOrWhiteSpace(readmeEntry)
                ? string.Empty
                : "<readme>" + readmeEntry + "</readme>";

            string nuspec =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                + "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\">"
                + "<metadata>"
                + "<id>Test.Package</id>"
                + "<version>1.0.0</version>"
                + "<authors>Test</authors>"
                + "<description>Package for README tests</description>"
                + readmeElement
                + "</metadata></package>";

            WriteEntry(archive, "Test.Package.nuspec", nuspec);
            foreach (KeyValuePair<string, string> file in files)
                WriteEntry(archive, file.Key, file.Value);
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

    /// <summary>Источник без сети: отдаёт заданные пакеты и считает скачивания.</summary>
    private sealed class FakeSource : NuGetSearchService
    {
        private readonly Dictionary<string, byte[]> _packages = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public FakeSource()
            : base("https://127.0.0.1:1/index.json")
        {
        }

        public Dictionary<string, NuGetPackageInfo> Packages { get; } =
            new Dictionary<string, NuGetPackageInfo>(StringComparer.OrdinalIgnoreCase);

        public List<string> Downloads { get; } = new List<string>();

        public void Add(string id, string version, byte[] nupkg)
        {
            _packages[id + "|" + version] = nupkg;
        }

        public override Task<NuGetPackageInfo> GetPackageInfoAsync(string packageId, bool includePrerelease = false)
        {
            return Task.FromResult(Packages.TryGetValue(packageId, out NuGetPackageInfo info) ? info : null);
        }

        public override Task<List<NuGetPackageVersionInfo>> GetVersionInfosAsync(
            string packageId,
            string installedVersion,
            bool includePrerelease = false)
        {
            var versions = new List<NuGetPackageVersionInfo>();
            if (Packages.TryGetValue(packageId, out NuGetPackageInfo info))
            {
                versions.Add(new NuGetPackageVersionInfo
                {
                    Version = info.Version,
                    Label = info.Version,
                    IsLatest = true,
                    IsLatestStable = true
                });
            }

            return Task.FromResult(versions);
        }

        public override Task<byte[]> DownloadNupkgAsync(string packageId, string version, CancellationToken cancellationToken = default)
        {
            Downloads.Add(packageId + "|" + version);
            return Task.FromResult(_packages.TryGetValue(packageId + "|" + version, out byte[] nupkg) ? nupkg : null);
        }
    }

    /// <summary>Загрузчик, который всегда падает: тест не должен ходить в реальный nuget.org.</summary>
    private sealed class ThrowingDownloader : NuGetPackageDownloader
    {
        public override Task<byte[]> DownloadNupkgAsync(string id, string version)
        {
            throw new InvalidOperationException("network is off in this test");
        }
    }
}
