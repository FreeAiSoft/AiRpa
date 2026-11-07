using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using RPA.NuGet.Models;
using RPA.NuGet.Services;
using RPA.NuGet.ViewModels;

namespace AiRpaStudio.Tests;

/// <summary>
/// Третья приёмка панели NuGet: ширина комбобоксов по содержимому, шеврон-иконка MaterialDesign,
/// иконочные кнопки плашки с зарезервированным местом и загрузка логотипов пакетов.
/// </summary>
[TestFixture]
public class NuGetPanelLayoutTests
{
    [Test]
    public void ComboBoxStyle_SizesToContentWithMaterialChevron()
    {
        string styles = Read(@"RPA.NuGet\Views\NuGetPanel.Styles.xaml");
        string panel = Read(@"RPA.NuGet\Views\NuGetPanel.xaml");

        Assert.Multiple(() =>
        {
            // Ширина по содержимому: растянутый комбобокс обрезал подпись версии.
            Assert.That(styles, Does.Contain("HorizontalAlignment\" Value=\"Left\""),
                "комбобокс обязан сжиматься до содержимого, а не растягиваться на колонку");
            Assert.That(styles, Does.Not.Contain("MinWidth\" Value=\"50\""),
                "минимальная ширина больше не задаётся в стиле — её задаёт разметка");

            // Шеврон — иконка MaterialDesign и стоит слева.
            Assert.That(styles, Does.Contain("materialIcons:MaterialIcon"), "шеврон обязан быть MaterialIcon");
            Assert.That(styles, Does.Contain("Kind=\"ChevronDown\""), "нет иконки ChevronDown");
            Assert.That(styles, Does.Contain("Text=\"{Binding SelectedItem, RelativeSource={RelativeSource AncestorType=ComboBox}}\""),
                "текст выбранного элемента обязан рисоваться TextBlock'ом: SelectionBoxItem пуст при DisplayMemberPath");
            Assert.That(styles, Does.Not.Contain("Content=\"{Binding SelectionBoxItem"),
                "ContentPresenter с SelectionBoxItem даёт пустое поле (регрессия «комбобокс пустой и маленький»)");

            int iconIndex = styles.IndexOf("Kind=\"ChevronDown\"", StringComparison.Ordinal);
            int textIndex = styles.IndexOf("Text=\"{Binding SelectedItem", StringComparison.Ordinal);
            Assert.That(iconIndex, Is.GreaterThan(0), "иконка ChevronDown не найдена");
            Assert.That(textIndex, Is.GreaterThan(iconIndex),
                "шеврон стоит слева от текста выбранного элемента");
            Assert.That(styles, Does.Contain("Grid.Column=\"0\""), "у иконки первая колонка");

            // Место под кнопки плашки выделено всегда.
            Assert.That(panel, Does.Contain("<ColumnDefinition Width=\"37\" />"),
                "под кнопки плашки выделена фиксированная колонка, суженная в 1.5 раза: 56 → 37");
            Assert.That(panel, Does.Not.Contain("<ColumnDefinition Width=\"56\" />"),
                "прежняя ширина колонки 56px больше не используется");
            Assert.That(panel, Does.Contain("NuGet.RowIconButtonStyle"), "кнопки плашки — иконочные");
            Assert.That(panel, Does.Contain("Kind=\"Download\""), "кнопка установки — иконка Download");
            Assert.That(panel, Does.Contain("Kind=\"Close\""), "кнопка удаления — крестик");
            Assert.That(panel, Does.Not.Contain("Kind=\"Delete\""), "иконка Delete заменена крестиком");
            Assert.That(panel, Does.Not.Contain("Content=\"Install\""), "текстовых кнопок на плашке больше нет");
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Panel_ComboBoxWidthFollowsContent()
    {
        var model = new NuGetPanelViewModel();
        string directory = CreateProject();
        Assert.That(model.LoadProject(Path.Combine(directory, "Demo.csproj")), Is.True);

        var panel = new RPA.NuGet.Views.NuGetPanel(model);
        Window window = ShowInWindow(panel);

        try
        {
            model.BrowseItems.Add(new NuGetPackageItemViewModel(new NuGetPackageInfo
            {
                Id = "Newtonsoft.Json",
                Version = "13.0.4",
                Authors = "James Newton-King"
            }));
            model.SelectedItem = model.BrowseItems[0];
            panel.UpdateLayout();
            Pump();

            var sourceCombo = panel.FindName("SourceComboBox") as ComboBox;
            var versionCombo = panel.FindName("VersionComboBox") as ComboBox;

            Assert.That(sourceCombo, Is.Not.Null);
            Assert.That(versionCombo, Is.Not.Null);

            // Источник: поле показывает выбранное имя и потому имеет ширину по тексту, а не 50px.
            string sourceText = sourceCombo.SelectedItem?.ToString();
            Assert.That(sourceText, Is.Not.Null.And.Not.Empty, "у источника есть выбранное имя");
            double sourceTextWidth = MeasureText(sourceCombo, sourceText);
            Assert.That(sourceCombo.ActualWidth, Is.GreaterThan(sourceTextWidth + 20d),
                "поле источника обязано вмещать своё имя целиком (шелврон + отступы)");

            double shortWidth = versionCombo.ActualWidth;
            string shortLabel = versionCombo.SelectedItem?.ToString();
            Assert.That(shortWidth, Is.GreaterThan(MeasureText(versionCombo, shortLabel)),
                "короткая подпись версии помещается целиком");

            // Длинная подпись «Latest stable 13.0.4» расширяет поле, а не обрезается.
            var details = new NuGetPackageDetails { Id = "Newtonsoft.Json", Version = "13.0.4" };
            details.Versions.Add(new NuGetPackageVersionInfo { Version = "13.0.4", IsLatestStable = true, Label = "Latest stable 13.0.4" });
            model.Details.Load(details, model.BrowseItems[0], installedVersion: null, sourceName: "nuget.org");
            panel.UpdateLayout();
            Pump();

            double longWidth = versionCombo.ActualWidth;
            string longLabel = versionCombo.SelectedItem?.ToString();
            double longTextWidth = MeasureText(versionCombo, longLabel);

            Assert.Multiple(() =>
            {
                Assert.That(longWidth, Is.GreaterThan(shortWidth + 20d),
                    "длинная подпись расширяет поле версии");
                Assert.That(longWidth, Is.GreaterThan(longTextWidth + 20d),
                    "подпись версии не обрезается: ширина поля больше ширины текста");
            });
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void RowIconButtons_AreSizedForNarrowColumn()
    {
        // Колонка кнопок сужена в 1.5 раза (56 → 37): две кнопки по 24px в неё не помещаются,
        // поэтому размер кнопки плашки — 16×16 (16 + 16 + отступ 5 = ровно 37).
        string styles = Read(@"RPA.NuGet\Views\NuGetPanel.Styles.xaml");
        int start = styles.IndexOf("x:Key=\"NuGet.RowIconButtonStyle\"", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThan(0), "нет стиля кнопки плашки");

        int end = styles.IndexOf("</Style>", start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start));
        string block = styles.Substring(start, end - start);

        Assert.Multiple(() =>
        {
            Assert.That(block, Does.Contain("Value=\"16\""), "кнопка плашки — 16×16");
            Assert.That(block, Does.Not.Contain("Value=\"24\""), "прежний размер 24×24 не влезает в узкую колонку");
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Panel_RowButtonsFitNarrowColumn()
    {
        // Обе кнопки плашки (установка и удаление-крестик) обязаны укладываться в суженную колонку
        // 37px: 16 + 16 + отступ 5 = ровно 37, иначе крестик выходил бы за правый край плашки.
        var model = new NuGetPanelViewModel(new StubSearchService());
        model.BrowseItems.Add(new NuGetPackageItemViewModel(new NuGetPackageInfo
        {
            Id = "Newtonsoft.Json",
            Version = "13.0.4",
            Authors = "James Newton-King",
            Description = "Json.NET is a popular high-performance JSON framework for .NET"
        }));

        var panel = new RPA.NuGet.Views.NuGetPanel(model);
        Window window = ShowInWindow(panel);

        try
        {
            panel.UpdateLayout();
            Pump();

            var list = panel.FindName("PackageList") as ListBox;
            Assert.That(list, Is.Not.Null);
            var row = list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
            Assert.That(row, Is.Not.Null, "строка списка создана");

            var action = FindDescendant<Button>(row, b => b.Name == "RowActionButton");
            var uninstall = FindDescendant<Button>(row, b => b.Name == "RowUninstallButton");
            Assert.That(action, Is.Not.Null, "кнопка установки есть в строке");
            Assert.That(uninstall, Is.Not.Null, "кнопка удаления есть в строке");

            action.Visibility = Visibility.Visible;
            uninstall.Visibility = Visibility.Visible;
            panel.UpdateLayout();
            Pump();

            double right = uninstall.TransformToAncestor(row).Transform(new Point(0, 0)).X + uninstall.ActualWidth;

            Assert.Multiple(() =>
            {
                Assert.That(action.ActualWidth, Is.EqualTo(16d).Within(0.5), "кнопка установки — 16px");
                Assert.That(uninstall.ActualWidth, Is.EqualTo(16d).Within(0.5), "кнопка удаления — 16px");
                Assert.That(right, Is.LessThanOrEqualTo(row.ActualWidth + 0.5),
                    "крестик не выходит за правый край плашки");
            });
        }
        finally
        {
            window.Close();
        }
    }

    private static double MeasureText(ComboBox comboBox, string text)
    {
        var probe = new TextBlock
        {
            Text = text ?? string.Empty,
            FontSize = comboBox.FontSize,
            FontFamily = comboBox.FontFamily,
            FontStyle = comboBox.FontStyle,
            FontWeight = comboBox.FontWeight
        };

        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Panel_RowButtonsDoNotShiftLayoutOnHover()
    {
        var model = new NuGetPanelViewModel(new StubSearchService());
        model.BrowseItems.Add(new NuGetPackageItemViewModel(new NuGetPackageInfo
        {
            Id = "Newtonsoft.Json",
            Version = "13.0.4",
            Authors = "James Newton-King",
            Description = "Json.NET is a popular high-performance JSON framework for .NET"
        }));

        var panel = new RPA.NuGet.Views.NuGetPanel(model);
        Window window = ShowInWindow(panel);

        try
        {
            panel.UpdateLayout();
            Pump();

            var list = panel.FindName("PackageList") as ListBox;
            Assert.That(list, Is.Not.Null);
            Assert.That(list.Items.Count, Is.EqualTo(1));

            var row = list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
            Assert.That(row, Is.Not.Null, "строка списка создана");

            double before = row.ActualWidth;
            var button = FindDescendant<Button>(row, b => b.Name == "RowActionButton");
            Assert.That(button, Is.Not.Null, "кнопка установки есть в строке");
            Assert.That(button.Visibility, Is.EqualTo(Visibility.Collapsed), "по умолчанию кнопка скрыта");

            // Место под кнопки выделено фиксированной колонкой: показываем кнопку и убеждаемся,
            // что ширина строки не изменилась (компоновка остальных элементов не «прыгает»).
            button.Visibility = Visibility.Visible;
            panel.UpdateLayout();
            Pump();

            Assert.Multiple(() =>
            {
                Assert.That(button.IsVisible, Is.True, "кнопка установки отрисована");
                Assert.That(row.ActualWidth, Is.EqualTo(before).Within(0.5),
                    "появление кнопки не меняет ширину строки — место под неё выделено заранее");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void IconUrl_FallsBackToNuGetFlatContainerPath()
    {
        // Источник не отдал IconUrl — панель обязана знать стандартный путь (без .png: он даёт 404).
        var withoutUrl = new NuGetPackageItemViewModel(new NuGetPackageInfo { Id = "Newtonsoft.Json", Version = "13.0.4" });

        Assert.That(withoutUrl.IconSourceUrl,
            Is.EqualTo("https://api.nuget.org/v3-flatcontainer/newtonsoft.json/13.0.4/icon"));

        var withUrl = new NuGetPackageItemViewModel(new NuGetPackageInfo
        {
            Id = "Newtonsoft.Json",
            Version = "13.0.4",
            IconUrl = "https://example.org/icon.png"
        });

        Assert.That(withUrl.IconSourceUrl, Is.EqualTo("https://example.org/icon.png"),
            "адрес источника имеет приоритет над стандартным");

        Assert.That(NuGetIconService.BuildDefaultIconUrl(null, "1.0.0"), Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public async Task IconService_LoadsIconFromDownloadedBytes()
    {
        // Путь «байты → картинка» проверяется без сети: заглушка отдаёт настоящий PNG.
        byte[] png = CreatePng(8, 8);
        ImageSource image = NuGetIconService.CreateImage(png);

        Assert.That(image, Is.Not.Null, "PNG из байтов обязан превращаться в картинку");
        Assert.That(image.Width, Is.EqualTo(8d).Within(0.1), "картинка создана из байтов");

        await Task.CompletedTask;
    }

    [Test]
    public async Task IconService_ReportsNetworkFailureInsteadOfSilence()
    {
        // Недоступный хост: иконки нет, но причина записана — панель больше не «молчит».
        var icons = new NuGetIconService();
        ImageSource image = await icons.LoadAsync("https://127.0.0.1:1/icon");

        Assert.Multiple(() =>
        {
            Assert.That(image, Is.Null);
            Assert.That(icons.LastError, Is.Not.Null.And.Not.Empty, "ошибка загрузки иконки обязана быть видна");
        });
    }

    // ------------------------------------------- внутренние ToggleButton шаблонов

    [Test]
    public void ComboBoxInnerToggleButtons_HaveExplicitStretchedStyle()
    {
        string styles = Read(@"RPA.NuGet\Views\NuGetPanel.Styles.xaml");

        // Причина «комбобоксы не растягиваются»: App.xaml подключает MaterialDesign2.Defaults.xaml,
        // где НЕЯВНЫЙ стиль ToggleButton задаёт Width = 34. Пока у внутренней коробки комбобокса был
        // только локальный Template, ширину 34 задавал этот чужой стиль, и подпись в неё не влезала
        // при любых значениях HorizontalAlignment, колонок Grid и TextTrimming.
        const string marker = "Style=\"{StaticResource NuGet.StretchedToggleButtonStyle}\"";
        int explicitStyles = styles.Split(new[] { marker }, StringSplitOptions.None).Length - 1;

        Assert.Multiple(() =>
        {
            Assert.That(styles, Does.Contain("x:Key=\"NuGet.StretchedToggleButtonStyle\""),
                "нет базового стиля внутренних ToggleButton");
            Assert.That(styles, Does.Contain("Property=\"Width\" Value=\"Auto\""),
                "ширину коробки обязан определять шаблон/колонка, а не неявный стиль MaterialDesign");
            Assert.That(styles, Does.Contain("Property=\"MinWidth\" Value=\"0\""),
                "MinWidth=\"Auto\" недопустим (NaN валит Style.Seal) — нужен 0");
            Assert.That(styles, Does.Contain("Property=\"HorizontalAlignment\" Value=\"Stretch\""),
                "коробка обязана растягиваться по комбобоксу");
            Assert.That(explicitStyles, Is.EqualTo(2),
                "явный Style обязаны иметь и коробка комбобокса (DropDownToggle), и шапка Expander (HeaderToggle)");
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Panel_ComboBoxesSurviveImplicitMaterialDesignToggleStyle()
    {
        var model = new NuGetPanelViewModel();
        string directory = CreateProject();
        Assert.That(model.LoadProject(Path.Combine(directory, "Demo.csproj")), Is.True);

        var panel = new RPA.NuGet.Views.NuGetPanel(model);

        // Эмуляция App.xaml: там подключён MaterialDesign2.Defaults.xaml с неявным стилем ToggleButton
        // (переключатель Width = 34). Панель в тестах хостится без App.xaml, поэтому неявный стиль
        // ставится руками — иначе проверка зелёная на дефекте, ради которого она написана.
        var implicitStyle = new Style(typeof(ToggleButton));
        implicitStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty, 34d));
        implicitStyle.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left));

        var window = new Window
        {
            Width = 1100,
            Height = 700,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Left = -3000,
            Top = -3000,
            ShowActivated = false
        };
        window.Resources.Add(typeof(ToggleButton), implicitStyle);
        window.Content = panel;

        window.Show();
        panel.UpdateLayout();
        Pump();

        try
        {
            model.BrowseItems.Add(new NuGetPackageItemViewModel(new NuGetPackageInfo
            {
                Id = "Newtonsoft.Json",
                Version = "13.0.4",
                Authors = "James Newton-King"
            }));
            model.SelectedItem = model.BrowseItems[0];
            panel.UpdateLayout();
            Pump();

            var sourceCombo = panel.FindName("SourceComboBox") as ComboBox;
            var versionCombo = panel.FindName("VersionComboBox") as ComboBox;
            Assert.That(sourceCombo, Is.Not.Null);
            Assert.That(versionCombo, Is.Not.Null);

            // В шаблоне комбобокса ToggleButton один — это и есть «коробка» поля.
            ToggleButton sourceBox = FindDescendant<ToggleButton>(sourceCombo, _ => true);
            ToggleButton versionBox = FindDescendant<ToggleButton>(versionCombo, _ => true);

            Assert.That(sourceBox, Is.Not.Null, "коробка комбобокса источника не найдена");
            Assert.That(versionBox, Is.Not.Null, "коробка комбобокса версии не найдена");

            string sourceText = sourceCombo.SelectedItem?.ToString();
            string versionText = versionCombo.SelectedItem?.ToString();

            Assert.Multiple(() =>
            {
                Assert.That(sourceBox.Style, Is.Not.Null,
                    "у внутреннего ToggleButton обязан быть явный Style: иначе ширину 34 задаёт MaterialDesign");
                Assert.That(sourceBox.ActualWidth, Is.EqualTo(sourceCombo.ActualWidth).Within(1.5),
                    "коробка обязана занимать всю ширину комбобокса источника");
                Assert.That(versionBox.ActualWidth, Is.EqualTo(versionCombo.ActualWidth).Within(1.5),
                    "коробка обязана занимать всю ширину комбобокса версии");
                Assert.That(sourceCombo.ActualWidth, Is.GreaterThan(MeasureText(sourceCombo, sourceText) + 20d),
                    "поле источника вмещает своё имя целиком — неявный стиль его больше не сжимает");
                Assert.That(versionCombo.ActualWidth, Is.GreaterThan(MeasureText(versionCombo, versionText)),
                    "поле версии вмещает подпись выбранной версии");
                Assert.That(sourceCombo.ActualWidth, Is.LessThanOrEqualTo(150.5), "потолок ширины — 150px");
                Assert.That(versionCombo.ActualWidth, Is.LessThanOrEqualTo(150.5), "потолок ширины — 150px");
            });
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, true);
        }
    }


    // ================= служебное =================

    private static byte[] CreatePng(int width, int height)
    {
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using (var stream = new MemoryStream())
        {
            encoder.Save(stream);
            return stream.ToArray();
        }
    }

    private static string CreateProject()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nuget-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Demo.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup /></Project>");
        return directory;
    }

    private static Window ShowInWindow(FrameworkElement content, double width = 1100, double height = 700)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = content,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Left = -3000,
            Top = -3000,
            ShowActivated = false
        };

        window.Show();
        content.UpdateLayout();
        Pump();
        return window;
    }

    private static void Pump()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static T FindDescendant<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        if (root == null)
            return null;

        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T typed && predicate(typed))
                return typed;

            T found = FindDescendant(child, predicate);
            if (found != null)
                return found;
        }

        return null;
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

    private sealed class StubSearchService : NuGetSearchService
    {
        public StubSearchService()
            : base("https://127.0.0.1:1/index.json")
        {
        }

        public override Task<List<NuGetPackageInfo>> SearchAsync(
            string query,
            int take = 20,
            bool includePrerelease = false,
            int skip = 0)
        {
            return Task.FromResult(new List<NuGetPackageInfo>
            {
                new NuGetPackageInfo { Id = "Newtonsoft.Json", Version = "13.0.4" }
            });
        }
    }
}
