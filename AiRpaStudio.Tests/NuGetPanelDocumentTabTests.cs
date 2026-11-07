using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using RPA.NuGet.Models;
using RPA.NuGet.Services;
using RPA.NuGet.ViewModels;
using RPA.NuGet.Views;

namespace AiRpaStudio.Tests;

/// <summary>
/// Размещение панели NuGet и её живая загрузка.
/// <para>
/// По уточнению заказчика менеджер пакетов открывается вкладкой <b>панели документов</b> (как в
/// Visual Studio), а не в узком боковом доке: там помещается двухколоночный макет. Вкладка обязана
/// восстанавливаться из сохранённой раскладки — поэтому ветка NuGet в
/// <c>MainView.InitializeLayoutRoot</c> стоит до отмены <c>LayoutDocument</c>.
/// </para>
/// </summary>
[TestFixture]
public class NuGetPanelDocumentTabTests
{
    [Test]
    public void MainView_OpensNuGetAsDocumentTab()
    {
        string code = Read(@"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("private const string NuGetContentId = \"NuGet\";"),
                "идентификатор вкладки NuGet обязан быть константой — по нему вкладка находится повторно");
            Assert.That(code, Does.Contain("ContentId = NuGetContentId"), "вкладка NuGet создаётся как LayoutDocument");
            Assert.That(code, Does.Contain("docGroup.Children.Add(document);"),
                "вкладка добавляется в общую панель документов");
            Assert.That(code, Does.Contain("FindNuGetDocument()"),
                "повторное открытие обязано переиспользовать вкладку, а не создавать вторую");
        });
    }

    [Test]
    public void MainView_DoesNotDockNuGetInSidePane()
    {
        string code = Read(@"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Not.Contain("nuGetAnchorable"),
                "панель больше не живёт в правой боковой группе (LayoutAnchorable)");
            Assert.That(code, Does.Not.Contain("Children.Add(nuGetPane);"),
                "раскладка по умолчанию не должна создавать боковую панель NuGet");
        });
    }

    [Test]
    public void MainView_RestoresNuGetBeforeCancellingDocuments()
    {
        string code = Read(@"AiRpaStudio\Views\MainView.xaml.cs");

        int nugetBranch = code.IndexOf("contentId == \"NuGet\"", StringComparison.Ordinal);
        int cancelDocuments = code.IndexOf("// Documents will be managed by docGroupHelper", StringComparison.Ordinal);
        int cancelLine = code.IndexOf("args.Cancel = true;", cancelDocuments, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(nugetBranch, Is.GreaterThan(0), "в раскладке нет ветки восстановления NuGet");
            Assert.That(cancelDocuments, Is.GreaterThan(nugetBranch),
                "ветка NuGet обязана стоять до отмены документов, иначе вкладка потеряется");
            Assert.That(cancelLine, Is.GreaterThan(nugetBranch));
            Assert.That(code, Does.Contain("nuGetContent = args.Model as LayoutContent;"),
                "сохранённая вкладка (или док) запоминается, чтобы панель не пересоздавалась");
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Panel_InitializesStylesAndNamedParts()
    {
        // Живая проверка: словарь NuGetPanel.Styles.xaml подключён корректным pack-URI, все шаблоны
        // собираются, обязательные именованные части на месте. Ошибка в шаблоне (или в Source
        // словаря) валит этот тест, а не запуск студии.
        var panel = new NuGetPanel();

        Assert.That(panel.ViewModel, Is.Not.Null, "панель обязана создать модель представления");
        Assert.That(panel.DataContext, Is.SameAs(panel.ViewModel));

        var search = panel.FindName("SearchTextBox") as TextBox;
        var list = panel.FindName("PackageList") as ListBox;
        var details = panel.FindName("DetailsPanel") as Border;

        Assert.Multiple(() =>
        {
            Assert.That(search, Is.Not.Null, "нет поля поиска");
            Assert.That(list, Is.Not.Null, "нет списка пакетов");
            Assert.That(details, Is.Not.Null, "нет панели деталей");
            Assert.That(list.Style, Is.Not.Null, "стиль списка не применился — словарь стилей не подключён");
            Assert.That(list.ItemContainerStyle, Is.Not.Null, "нет стиля строки пакета");
        });

        // Прогон раскладки: шаблоны (ComboBox, Expander, CheckBox, ProgressBar) строятся здесь.
        panel.Measure(new Size(1100, 700));
        panel.Arrange(new Rect(0, 0, 1100, 700));
        panel.UpdateLayout();

        Assert.That(panel.ViewModel.IsCompact, Is.False, "широкая панель — двухколоночный режим");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Panel_ComboBoxes_OpenDropDown()
    {
        // Регрессия: список источников и список версий не раскрывались. Причина была в шаблоне —
        // визуал выносился из ToggleButton, и прозрачный слой клика отбирал захват мыши, из-за чего
        // IsDropDownOpen сразу возвращался в false.
        // Popup существует только в окне: панель хостится в реальном Window, иначе PresentationSource нет.
        var model = new NuGetPanelViewModel(new NuGetSearchService("https://127.0.0.1:1/index.json"));
        var panel = new NuGetPanel(model);

        model.BrowseItems.Add(new NuGetPackageItemViewModel(new NuGetPackageInfo
        {
            Id = "Newtonsoft.Json",
            Version = "13.0.4",
            Authors = "James Newton-King"
        }));

        Window window = ShowInWindow(panel);

        try
        {
            model.SelectedItem = model.BrowseItems[0];
            panel.UpdateLayout();
            Pump();

            var sourceCombo = panel.FindName("SourceComboBox") as ComboBox;
            var versionCombo = panel.FindName("VersionComboBox") as ComboBox;

            Assert.That(sourceCombo, Is.Not.Null);
            Assert.That(versionCombo, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(TryOpenDropDown(sourceCombo), Is.True, "комбобокс источника не раскрывается");
                Assert.That(TryOpenDropDown(versionCombo), Is.True, "комбобокс версий не раскрывается");
            });
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Хостит панель в окне (нужно для Popup и корректной раскладки).</summary>
    private static Window ShowInWindow(NuGetPanel panel, double width = 1100, double height = 700)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = panel,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Left = -3000,
            Top = -3000,
            ShowActivated = false
        };

        window.Show();
        panel.UpdateLayout();
        Pump();
        return window;
    }

    /// <summary>Прокручивает очередь диспетчера: попап и раскладка обновляются асинхронно.</summary>
    private static void Pump()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    /// <summary>Раскрывает список комбобокса и сообщает, открылся ли Popup шаблона.</summary>
    private static bool TryOpenDropDown(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        comboBox.IsDropDownOpen = true;
        comboBox.UpdateLayout();
        Pump();

        var popup = comboBox.Template?.FindName("PART_Popup", comboBox) as System.Windows.Controls.Primitives.Popup;
        bool opened = popup != null && popup.IsOpen;

        comboBox.IsDropDownOpen = false;
        comboBox.UpdateLayout();
        Pump();
        return opened;
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Panel_WithoutSelection_HasNoDetailsColumn()
    {
        // Замечание по правой колонке: пока пакет не выбран, обзора быть не должно вовсе —
        // ни пустой колонки, ни «скелета».
        var panel = new NuGetPanel();
        panel.Measure(new Size(1100, 700));
        panel.Arrange(new Rect(0, 0, 1100, 700));
        panel.UpdateLayout();

        var detailsColumn = panel.FindName("DetailsColumn") as ColumnDefinition;
        var splitterColumn = panel.FindName("SplitterColumn") as ColumnDefinition;
        var detailsPanel = panel.FindName("DetailsPanel") as Border;

        Assert.Multiple(() =>
        {
            Assert.That(panel.ViewModel.HasSelection, Is.False);
            Assert.That(detailsColumn, Is.Not.Null);
            Assert.That(detailsPanel, Is.Not.Null);
            Assert.That(detailsColumn.Width.Value, Is.EqualTo(0d), "правая колонка не занимает места без выбора");
            Assert.That(splitterColumn.Width.Value, Is.EqualTo(0d), "разделитель без выбора тоже не нужен");
            Assert.That(detailsPanel.Visibility, Is.EqualTo(Visibility.Collapsed), "обзор пакета скрыт");
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Panel_WithoutProject_ExplainsWhatToDo()
    {
        var panel = new NuGetPanel();
        NuGetPanelViewModel model = panel.ViewModel;

        Assert.Multiple(() =>
        {
            Assert.That(model.HasProject, Is.False);
            Assert.That(model.ActiveTab, Is.EqualTo(NuGetPackageTab.Browse));
            Assert.That(model.IsBrowseTab, Is.True);
            Assert.That(model.EmptyText, Does.Contain("Open a project"));
            Assert.That(model.ProjectHeader, Is.EqualTo("NuGet Package Manager"));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Panel_RendersSelectedPackageAndDetails()
    {
        // Модель на заведомо недоступном источнике: живой рендер без сети. Проверяет, что шаблон
        // строки, ComboBox версии, Expander и вкладки собираются, а панель деталей появляется
        // сразу с выбранной версией и активной кнопкой действия.
        var model = new NuGetPanelViewModel(new NuGetSearchService("https://127.0.0.1:1/index.json"));
        var panel = new NuGetPanel(model);

        model.BrowseItems.Add(new NuGetPackageItemViewModel(new NuGetPackageInfo
        {
            Id = "Microsoft.EntityFrameworkCore.SqlServer",
            Version = "10.0.14",
            Authors = "Microsoft",
            Description = "Microsoft SQL Server database provider for Entity Framework Core.",
            TotalDownloads = 8360000000
        }));

        panel.Measure(new Size(1100, 700));
        panel.Arrange(new Rect(0, 0, 1100, 700));
        panel.UpdateLayout();

        model.SelectedItem = model.BrowseItems[0];
        panel.UpdateLayout();

        var detailsColumn = panel.FindName("DetailsColumn") as ColumnDefinition;
        var detailsPanel = panel.FindName("DetailsPanel") as Border;

        var sourceCombo = panel.FindName("SourceComboBox") as ComboBox;
        var versionCombo = panel.FindName("VersionComboBox") as ComboBox;

        Assert.Multiple(() =>
        {
            Assert.That(detailsColumn.Width.Value, Is.GreaterThan(0d), "выбран пакет — правая колонка появилась");
            Assert.That(detailsPanel.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(model.Details.SelectedVersion, Is.Not.Null,
                "версия выбрана сразу из карточки, без ожидания источника");
            Assert.That(model.Details.Action, Is.Not.EqualTo(NuGetPackageAction.None),
                "кнопка действия активна");
            Assert.That(model.Details.ShowReadmeTab, Is.False, "README ещё не прочитан — вкладки нет");

            // Жалоба «комбобоксы сжались»: ширина источника ограничена 50..150, у версии — не меньше 80,
            // и в обоих виден текст выбранного элемента.
            Assert.That(sourceCombo, Is.Not.Null);
            Assert.That(versionCombo, Is.Not.Null);
            Assert.That(sourceCombo.ActualWidth, Is.InRange(50d, 150d), "источник: ширина 50..150");
            Assert.That(versionCombo.ActualWidth, Is.GreaterThanOrEqualTo(80d), "версия: не уже 80");
        });

        var bitmap = new RenderTargetBitmap(1100, 700, 96, 96, PixelFormats.Pbgra32);
        Assert.DoesNotThrow(() => bitmap.Render(panel), "разметка панели обязана отрисовываться");
        Assert.That(bitmap.PixelWidth, Is.EqualTo(1100));
    }

    [Test, Apartment(ApartmentState.STA)]
    public async Task Panel_ShowsPackageIconFromLoader()
    {
        // Жалоба «логотип пакета не отображается»: иконка грузится через NuGetIconService, который
        // здесь подменён заглушкой. Проверяется весь путь: карточка → LoadIconAsync → HasIcon.
        var icons = new StubIconService();
        var model = new NuGetPanelViewModel(
            new NuGetSearchService("https://127.0.0.1:1/index.json"),
            icons: icons);

        var item = new NuGetPackageItemViewModel(new NuGetPackageInfo
        {
            Id = "Newtonsoft.Json",
            Version = "13.0.4",
            IconUrl = "https://example.org/json.png",
            Authors = "James Newton-King"
        }, icons.LoadAsync);

        model.BrowseItems.Add(item);
        await item.LoadIconAsync();

        Assert.Multiple(() =>
        {
            Assert.That(icons.Requests, Is.EqualTo(1), "запрос иконки выполнен");
            Assert.That(item.HasIcon, Is.True, "иконка пакета должна отображаться");
            Assert.That(item.Icon, Is.Not.Null);
        });

        var panel = new NuGetPanel(model);
        panel.Measure(new Size(1100, 700));
        panel.Arrange(new Rect(0, 0, 1100, 700));
        panel.UpdateLayout();

        model.SelectedItem = item;
        panel.UpdateLayout();

        Assert.That(model.Details.Item.HasIcon, Is.True, "логотип в шапке деталей берётся из карточки");
    }

    /// <summary>Загрузчик иконок без сети: отдаёт пустую картинку 1×1.</summary>
    private sealed class StubIconService : NuGetIconService
    {
        public int Requests { get; private set; }

        public override System.Threading.Tasks.Task<ImageSource> LoadAsync(string iconUrl)
        {
            Requests++;
            var bitmap = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null);
            bitmap.Freeze();
            return System.Threading.Tasks.Task.FromResult<ImageSource>(bitmap);
        }
    }

    /// <summary>
    /// Диагностика дизайна: сохраняет отрисованную панель в PNG (путь — переменная окружения
    /// <c>NUGET_PANEL_PREVIEW</c>). Тест явный и в обычном прогоне не выполняется.
    /// </summary>
    [Test, Explicit("Диагностика: пишет PNG по пути из NUGET_PANEL_PREVIEW"), Apartment(ApartmentState.STA)]
    public void Preview_RendersPanelToFile()
    {
        string target = Environment.GetEnvironmentVariable("NUGET_PANEL_PREVIEW");
        Assert.That(target, Is.Not.Null.And.Not.Empty, "задайте NUGET_PANEL_PREVIEW=<путь>.png");

        var model = new NuGetPanelViewModel(new NuGetSearchService("https://127.0.0.1:1/index.json"));
        var panel = new NuGetPanel(model);

        string[] ids =
        {
            "Microsoft.EntityFrameworkCore.SqlServer",
            "Microsoft.EntityFrameworkCore",
            "Newtonsoft.Json",
            "Microsoft.EntityFrameworkCore.Tools",
            "Microsoft.EntityFrameworkCore.Design",
            "Microsoft.Extensions.DependencyInjection",
            "Microsoft.Extensions.Logging"
        };

        for (int i = 0; i < ids.Length; i++)
        {
            model.BrowseItems.Add(new NuGetPackageItemViewModel(new NuGetPackageInfo
            {
                Id = ids[i],
                Version = i == 2 ? "13.0.4" : "10.0.14",
                Authors = i == 2 ? "James Newton-King" : "Microsoft",
                Description = i == 2
                    ? "Json.NET is a popular high-performance JSON framework for .NET"
                    : "Microsoft SQL Server database provider for Entity Framework Core.",
                TotalDownloads = i == 6 ? 8360000000 : (i == 2 ? 5000000000 : 120000000)
            }));
        }

        const double width = 1100;
        const double height = 700;
        panel.Measure(new Size(width, height));
        panel.Arrange(new Rect(0, 0, width, height));
        panel.UpdateLayout();

        model.SelectedItem = model.BrowseItems[0];
        panel.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(panel);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = System.IO.File.Create(target))
        {
            encoder.Save(stream);
        }
    }

    private static string Read(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"не найден файл {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }
}
