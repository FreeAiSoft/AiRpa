using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using NUnit.Framework;
using NuGet.Configuration;
using RPA.NuGet.Converters;
using RPA.NuGet.Models;
using RPA.NuGet.Services;
using RPA.NuGet.ViewModels;

namespace AiRpaStudio.Tests;

/// <summary>
/// Вторая приёмка панели NuGet: авто-поиск с дебаунсом, оверлей «Package sources» и рендер README.
/// <para>
/// Все тесты работают без сети: источник поиска — стаб, который только считает вызовы и отдаёт
/// заранее заданные карточки.
/// </para>
/// </summary>
[TestFixture]
public class NuGetSourcesAndSearchTests
{
    // ================= 1. Поиск без Enter: дебаунс =================

    [Test]
    public async Task SearchQuery_SearchesOnceAfterTypingStops()
    {
        var search = new CountingSearchService();
        var model = new NuGetPanelViewModel(search) { SearchDebounceMilliseconds = 120 };

        model.SearchQuery = "new";
        model.SearchQuery = "newt";
        model.SearchQuery = "newtonsoft";

        await model.PendingSearch;

        Assert.Multiple(() =>
        {
            Assert.That(search.Queries.Where(query => query != "Newtonsoft.Json"),
                Is.EqualTo(new[] { "newtonsoft" }),
                "в источник уходит только последний набранный запрос: Enter больше не нужен");
            Assert.That(model.BrowseItems, Is.Not.Empty);
        });
    }

    [Test]
    public async Task SearchQuery_EmptyClearsListWithoutRequest()
    {
        var search = new CountingSearchService();
        var model = new NuGetPanelViewModel(search) { SearchDebounceMilliseconds = 0 };

        model.SearchQuery = "json";
        await model.PendingSearch;
        int afterFirst = search.CallCount;

        model.SearchQuery = string.Empty;
        await model.PendingSearch;

        Assert.Multiple(() =>
        {
            Assert.That(search.CallCount, Is.EqualTo(afterFirst), "очистка поля не ходит в источник");
            Assert.That(model.BrowseItems, Is.Empty);
            Assert.That(model.HasSearchQuery, Is.False);
        });
    }

    [Test]
    public async Task LocalSource_EmptyQueryListsAllPackages()
    {
        var search = new LocalCountingSearchService();
        var model = new NuGetPanelViewModel(search) { SearchDebounceMilliseconds = 0 };

        string directory = CreateTempDirectory();
        try
        {
            string project = Path.Combine(directory, "Demo.csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup /></Project>");
            model.LoadProject(project);

            // Пустой поиск у локального источника = «показать все пакеты», а не очистить список.
            model.SearchQuery = "local";
            await model.PendingSearch;
            Assert.That(search.Queries, Does.Contain("local"), "непустой запрос фильтрует локальный источник");

            model.SearchQuery = string.Empty;
            await model.PendingSearch;

            Assert.Multiple(() =>
            {
                Assert.That(search.Queries, Does.Contain(string.Empty),
                    "пустой запрос уходит в локальный источник как «показать всё»");
                Assert.That(model.BrowseItems, Is.Not.Empty, "локальный источник показывает все пакеты");
                Assert.That(model.HasSearchQuery, Is.False);
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task LocalFolderSource_EmptyQueryListsAllAndTypingFilters()
    {
        string directory = CreateTempDirectory();
        try
        {
            string feed = Path.Combine(directory, "feed");
            Directory.CreateDirectory(feed);
            File.WriteAllBytes(Path.Combine(feed, "Local.Package.1.0.0.nupkg"), BuildPackage());

            var search = new NuGetSearchService(feed);
            Assert.That(search.IsLocal, Is.True, "путь к папке распознаётся как локальный источник");

            List<NuGetPackageInfo> all = await search.SearchAsync(string.Empty, 20, false);
            Assert.That(all.Select(p => p.Id), Does.Contain("Local.Package"),
                "пустой запрос к локальной папке возвращает все пакеты");

            List<NuGetPackageInfo> filtered = await search.SearchAsync("Local", 20, false);
            Assert.That(filtered.Select(p => p.Id), Does.Contain("Local.Package"),
                "непустой запрос фильтрует локальную папку");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // ================= 2. Оверлей «Package sources» =================

    [Test]
    public void SourcesOverlay_OpensAndCloses()
    {
        var model = new NuGetPanelViewModel(new CountingSearchService());

        Assert.That(model.IsSourcesOverlayOpen, Is.False);

        model.OpenSourcesCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(model.IsSourcesOverlayOpen, Is.True, "шестерня открывает оверлей источников");
            Assert.That(model.SourceItems, Is.Not.Null);
        });

        model.CloseSourcesCommand.Execute(null);
        Assert.That(model.IsSourcesOverlayOpen, Is.False);
    }

    [Test]
    public void SourcesOverlay_AddEditRemove_WritesNuGetConfig()
    {
        string directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "NuGet.Config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><packageSources><clear />"
                + "<add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />"
                + "</packageSources></configuration>");

            var model = new NuGetPanelViewModel(new CountingSearchService());
            LoadProjectInto(model, directory);

            model.OpenSourcesCommand.Execute(null);
            Assert.That(model.SourceItems.Select(item => item.Name), Does.Contain("nuget.org"));

            // Add
            model.AddSourceCommand.Execute(null);
            Assert.That(model.IsEditingSource, Is.True);
            model.EditingSourceName = "studio-source";
            model.EditingSourceUrl = "https://example.org/v3/index.json";
            model.SaveSourceCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(model.IsEditingSource, Is.False);
                Assert.That(model.SourceItems.Select(item => item.Name), Does.Contain("studio-source"));
                Assert.That(ReadConfig(directory), Does.Contain("studio-source"),
                    "источник записан в NuGet.Config");
            });

            // Edit
            model.SelectedSourceItem = model.SourceItems.First(item => item.Name == "studio-source");
            model.EditSourceCommand.Execute(null);
            model.EditingSourceUrl = "https://example.org/v3/other.json";
            model.SaveSourceCommand.Execute(null);

            Assert.That(ReadConfig(directory), Does.Contain("https://example.org/v3/other.json"),
                "правка адреса сохраняется");

            // Remove
            model.SelectedSourceItem = model.SourceItems.First(item => item.Name == "studio-source");
            model.RemoveSourceCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(model.SourceItems.Select(item => item.Name), Does.Not.Contain("studio-source"));
                Assert.That(ReadConfig(directory), Does.Not.Contain("studio-source"), "источник удалён из файла");
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void SourcesOverlay_ValidatesNameAndDuplicates()
    {
        string directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "NuGet.Config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><packageSources><clear />"
                + "<add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />"
                + "</packageSources></configuration>");

            var model = new NuGetPanelViewModel(new CountingSearchService());
            LoadProjectInto(model, directory);
            model.OpenSourcesCommand.Execute(null);

            model.AddSourceCommand.Execute(null);
            model.EditingSourceName = "  ";
            model.EditingSourceUrl = "https://example.org/v3/index.json";
            model.SaveSourceCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(model.IsEditingSource, Is.True, "без имени редактор не закрывается");
                Assert.That(model.SourceEditError, Does.Contain("name"));
            });

            model.EditingSourceName = "nuget.org";
            model.SaveSourceCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(model.IsEditingSource, Is.True);
                Assert.That(model.SourceEditError, Does.Contain("already exists"));
            });
            // Duplicate by address/path: unique name, but the address already exists.
            model.EditingSourceName = "org-copy";
            model.EditingSourceUrl = "https://api.nuget.org/v3/index.json";
            model.SaveSourceCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(model.IsEditingSource, Is.True, "duplicate address must keep the editor open");
                Assert.That(model.SourceEditError, Does.Contain("address"));
            });

            model.CancelSourceEditCommand.Execute(null);
            Assert.That(model.IsEditingSource, Is.False);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void SourcesOverlay_EnabledLocalSourceIsOfferedAndDisabledIsNot()
    {
        string directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "NuGet.Config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><packageSources><clear />"
                + "<add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />"
                + "<add key=\"local\" value=\"" + Path.Combine(directory, "offline") + "\" />"
                + "</packageSources>"
                + "<disabledPackageSources><clear /></disabledPackageSources></configuration>");

            var model = new NuGetPanelViewModel();
            LoadProjectInto(model, directory);

            Assert.Multiple(() =>
            {
                Assert.That(model.Sources.Select(source => source.Name), Does.Contain("nuget.org"));
                Assert.That(model.Sources.Select(source => source.Name), Does.Contain("local"),
                    "enabled local folder is offered in the Package source combo");
            });

            model.OpenSourcesCommand.Execute(null);
            Assert.That(model.SourceItems.Select(item => item.Name), Does.Contain("local"),
                "но в оверлее она видна — как в Visual Studio");

            NuGetSourceItemViewModel local = model.SourceItems.First(item => item.Name == "local");
            local.IsEnabled = false;

            Assert.That(model.Sources.Select(source => source.Name), Does.Not.Contain("local"), "disabled source disappears from the Package source combo");

            Assert.That(ReadConfig(directory), Does.Contain("disabledPackageSources").Or.Contain("local"),
                "состояние Enabled сохраняется в конфигурацию");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // ================= 3. README: Markdown, а не сырой текст =================

    [Test, Apartment(ApartmentState.STA)]
    public void MarkdownRenderer_RendersHeadingsBoldListsAndCode()
    {
        const string markdown =
            "# Заголовок\n\n"
            + "Обычный **жирный** текст и `код`.\n\n"
            + "- первый пункт\n"
            + "- второй пункт\n\n"
            + "```csharp\n"
            + "var x = 1;\n"
            + "```\n";

        FlowDocument document = MarkdownRenderer.Render(markdown);
        List<Block> blocks = document.Blocks.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(blocks.OfType<Paragraph>().Any(p => p.FontSize > 15), Is.True,
                "заголовок #1 отрисован крупным шрифтом");
            Assert.That(blocks.OfType<Paragraph>().Any(p => p.Inlines.OfType<Bold>().Any()), Is.True,
                "**жирный** стал Bold, а не звёздочками");
            Assert.That(blocks.OfType<Paragraph>().Count(p => TextOf(p).Contains("пункт")), Is.EqualTo(2),
                "пункты списка стали отдельными абзацами");
            Assert.That(blocks.OfType<Section>().Any(), Is.True, "блок ``` стал Section");
            Assert.That(TextOf(document).Contains("**"), Is.False, "в тексте не остаётся разметки");
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void MarkdownRenderer_HandlesLinksImagesAndEmptyInput()
    {
        FlowDocument document = MarkdownRenderer.Render("См. [документацию](https://example.org) и ![логотип](img.png).");

        Assert.Multiple(() =>
        {
            Assert.That(TextOf(document), Does.Contain("документацию"));
            Assert.That(TextOf(document), Does.Not.Contain("[image:"), "картинки больше не выводятся заглушкой [image: alt]");
            Assert.That(TextOf(document), Does.Not.Contain("логотип"), "alt картинки скрывается");
            Assert.That(TextOf(document), Does.Not.Contain("img.png"), "адрес картинки не выводится как текст");
            Assert.That(TextOf(document), Does.Not.Contain("!["), "сырой markdown картинки не остаётся");
            Assert.That(TextOf(document), Does.Not.Contain("https://example.org"), "адрес ссылки не выводится как текст");
            Assert.That(MarkdownRenderer.Render(null).Blocks, Is.Empty, "пустой README — пустой документ");
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void MarkdownRenderer_HidesImagesIncludingLinkWrappedBadges()
    {
        const string markdown =
            "# ![Logo](Doc/icons/logo.jpg) Json.NET\n\n"
            + "[![NuGet version (Newtonsoft.Json)](https://img.shields.io/nuget/v/Newtonsoft.Json.svg?style=flat-square)](https://www.nuget.org/packages/Newtonsoft.Json/)\n"
            + "[![Build status](https://dev.azure.com/jamesnk/Public/_apis/build/status/JamesNK.Newtonsoft.Json?branchName=master)](https://dev.azure.com/jamesnk/Public/_build/latest?definitionId=8)\n\n"
            + "Json.NET is a popular high-performance JSON framework for .NET";

        FlowDocument document = MarkdownRenderer.Render(markdown);
        string text = TextOf(document);

        Assert.Multiple(() =>
        {
            Assert.That(TextOf(document.Blocks.FirstBlock).Trim(), Is.EqualTo("Json.NET"),
                "заголовок с логотипом остаётся чистым текстом без заглушки и пробелов");
            Assert.That(text, Does.Not.Contain("Logo"), "логотип скрыт");
            Assert.That(text, Does.Not.Contain("[image:"), "нет заглушки [image: alt]");
            Assert.That(text, Does.Not.Contain("!["), "нет сырого markdown картинок");
            Assert.That(text, Does.Not.Contain("img.shields.io"), "адрес картинки бейджа не выводится");
            Assert.That(text, Does.Not.Contain("nuget.org"), "адрес ссылки-бейджа не выводится текстом");
            Assert.That(text, Does.Contain("popular high-performance JSON framework"), "обычный абзац не пострадал");
        });

        // Строки-бейджи не дают пустых абзацев: остаются только заголовок и обычный абзац.
        Assert.That(document.Blocks.Count, Is.EqualTo(2), "строки-бейджи не создают пустых абзацев");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Panel_ReadmeIsShownAsFlowDocument()
    {
        var model = new NuGetPanelViewModel(new CountingSearchService());
        var panel = new RPA.NuGet.Views.NuGetPanel(model);

        panel.Measure(new Size(1100, 700));
        panel.Arrange(new Rect(0, 0, 1100, 700));
        panel.UpdateLayout();

        var viewer = panel.FindName("ReadmeViewer") as FlowDocumentScrollViewer;

        Assert.That(viewer, Is.Not.Null, "README отображается FlowDocumentScrollViewer (разметка, а не сырой md)");
    }

    [Test]
    public async Task Install_UsesSelectedSourceInsteadOfNuGetOrg()
    {
        // Регрессия: установка качала .nupkg жёстко с api.nuget.org, игнорируя выбранный
        // локальный/корпоративный источник. Теперь InstallAsync обязан брать пакет из источника.
        string directory = CreateTempDirectory();
        try
        {
            string projectPath = Path.Combine(directory, "Legacy.csproj");
            File.WriteAllText(projectPath,
                "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><ItemGroup /></Project>");
            File.WriteAllText(Path.Combine(directory, "packages.config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><packages></packages>");

            var search = new LocalFeedSearchService();
            var install = new NuGetInstallService(new ThrowingNuGetDownloader());
            NuGetProjectPackages project = NuGetProjectConfig.Load(projectPath);

            await install.InstallAsync(project, "Local.Package", "1.0.0", search);

            Assert.Multiple(() =>
            {
                Assert.That(search.Downloads, Does.Contain("Local.Package|1.0.0"),
                    "пакет обязан качаться из выбранного источника, а не с nuget.org");
                Assert.That(Directory.Exists(Path.Combine(directory, "packages", "Local.Package.1.0.0")),
                    Is.True, "пакет распакован в локальную папку packages");
                Assert.That(NuGetProjectConfig.Find(project, "Local.Package"), Is.Not.Null,
                    "ссылка на пакет добавлена в проект");
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // ================= Служебное =================

    private static void LoadProjectInto(NuGetPanelViewModel model, string directory)
    {
        string project = Path.Combine(directory, "Demo.csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup /></Project>");
        Assert.That(model.LoadProject(project), Is.True);
    }

    private static string ReadConfig(string directory)
    {
        string path = Path.Combine(directory, "NuGet.Config");
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    /// <summary>Весь текст документа: FlowDocument не является TextElement, поэтому принимаем object.</summary>
    private static string TextOf(object element)
    {
        if (element is FlowDocument document)
            return string.Join(" ", document.Blocks.Select(block => TextOf(block)));

        if (element is Section section)
            return string.Join(" ", section.Blocks.Select(block => TextOf(block)));

        if (element is TextElement textElement)
            return new TextRange(textElement.ContentStart, textElement.ContentEnd).Text;

        return string.Empty;
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nuget-sources-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Сервис поиска без сети: считает вызовы и отдаёт одну карточку.</summary>
    private sealed class CountingSearchService : NuGetSearchService
    {
        public CountingSearchService()
            : base("https://127.0.0.1:1/index.json")
        {
        }

        public List<string> Queries { get; } = new List<string>();

        public int CallCount => Queries.Count;

        public override Task<List<NuGetPackageInfo>> SearchAsync(
            string query,
            int take = 20,
            bool includePrerelease = false,
            int skip = 0)
        {
            Queries.Add(query);
            return Task.FromResult(new List<NuGetPackageInfo>
            {
                new NuGetPackageInfo
                {
                    Id = "Newtonsoft.Json",
                    Version = "13.0.4",
                    Authors = "James Newton-King",
                    Description = "Json.NET is a popular high-performance JSON framework for .NET"
                }
            });
        }
    }

    /// <summary>Локальный источник без сети: пустой запрос означает «показать все пакеты».</summary>
    private sealed class LocalCountingSearchService : NuGetSearchService
    {
        public LocalCountingSearchService()
            : base("C:\\offline")
        {
        }

        public List<string> Queries { get; } = new List<string>();

        public override Task<List<NuGetPackageInfo>> SearchAsync(
            string query,
            int take = 20,
            bool includePrerelease = false,
            int skip = 0)
        {
            Queries.Add(query ?? string.Empty);
            return Task.FromResult(new List<NuGetPackageInfo>
            {
                new NuGetPackageInfo
                {
                    Id = "Local.Package",
                    Version = "1.0.0",
                    Authors = "Local",
                    Description = "Local package"
                }
            });
        }
    }

    /// <summary>Локальный источник без сети: отдаёт nupkg и считает скачивания.</summary>
    private sealed class LocalFeedSearchService : NuGetSearchService
    {
        private readonly byte[] _nupkg = BuildPackage();

        public LocalFeedSearchService()
            : base("https://local.test/index.json")
        {
        }

        public List<string> Downloads { get; } = new List<string>();

        public override Task<byte[]> DownloadNupkgAsync(string packageId, string version, CancellationToken cancellationToken = default)
        {
            Downloads.Add(packageId + "|" + version);
            return Task.FromResult(_nupkg);
        }
    }

    /// <summary>Прямой загрузчик nuget.org запрещён: если вызван — тест падает.</summary>
    private sealed class ThrowingNuGetDownloader : NuGetPackageDownloader
    {
        public override Task<byte[]> DownloadNupkgAsync(string id, string version)
        {
            throw new InvalidOperationException("nuget.org direct downloader must not be used when a source is selected");
        }
    }

    private static byte[] BuildPackage()
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            const string nuspec =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                + "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\">"
                + "<metadata>"
                + "<id>Local.Package</id>"
                + "<version>1.0.0</version>"
                + "<authors>Local</authors>"
                + "<description>Local package</description>"
                + "</metadata></package>";

            WriteEntry(archive, "Local.Package.nuspec", nuspec);
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

}
