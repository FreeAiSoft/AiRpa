using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

            model.CancelSourceEditCommand.Execute(null);
            Assert.That(model.IsEditingSource, Is.False);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void SourcesOverlay_DisabledSourceIsNotOfferedForSearch()
    {
        string directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "NuGet.Config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><packageSources><clear />"
                + "<add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />"
                + "<add key=\"local\" value=\"" + Path.Combine(directory, "offline") + "\" />"
                + "</packageSources></configuration>");

            var model = new NuGetPanelViewModel();
            LoadProjectInto(model, directory);

            Assert.Multiple(() =>
            {
                Assert.That(model.Sources.Select(source => source.Name), Does.Contain("nuget.org"));
                Assert.That(model.Sources.Select(source => source.Name), Does.Not.Contain("local"),
                    "локальная папка не годится для поиска через NuGet.Protocol");
            });

            model.OpenSourcesCommand.Execute(null);
            Assert.That(model.SourceItems.Select(item => item.Name), Does.Contain("local"),
                "но в оверлее она видна — как в Visual Studio");

            NuGetSourceItemViewModel local = model.SourceItems.First(item => item.Name == "local");
            local.IsEnabled = false;

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
            Assert.That(TextOf(document), Does.Contain("[image: логотип]"), "картинки заменяются alt-текстом");
            Assert.That(TextOf(document), Does.Not.Contain("https://example.org"), "адрес ссылки не выводится как текст");
            Assert.That(MarkdownRenderer.Render(null).Blocks, Is.Empty, "пустой README — пустой документ");
        });
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
}
