using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using RPA.Controls;
using RPA.Views;
using ShellWindowChrome = System.Windows.Shell.WindowChrome;

namespace AiRpaStudio.Tests;

/// <summary>
/// Единый заголовок окон студии.
/// <para>
/// До задачи своя полоса заголовка была только у главного окна (<c>Views\MainView.xaml</c>,
/// 32px, кисти <c>Studio.TitleBar*</c>, кнопки 46×32 на <c>MaterialIcon</c>). Диалоги
/// («Создать активность», «Создать проект», «Открыть VS-проект», «Из VS-шаблона», окно ошибки)
/// рисовали системный caption Windows: он белый в любой теме и не скругляется вместе с окном
/// на Windows 10 (у диалогов не было <c>WindowChrome</c>, а region-fallback скругления работает
/// только при наличии chrome).
/// </para>
/// <para>
/// Требование: все окна рисуют заголовок сами тем же контролом
/// <see cref="StudioTitleBar"/> — высота 32, фон/текст только из палитры, кнопки
/// «свернуть»/«закрыть» 46×32 с иконками 16×16, перетаскивание окна за полосу.
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class StudioTitleBarTests
{
    private const string TitleBarXaml = @"AiRpaStudio\Controls\StudioTitleBar.xaml";
    private const string TitleBarCodeBehind = @"AiRpaStudio\Controls\StudioTitleBar.xaml.cs";

    private static readonly string[] DialogPaths =
    {
        @"AiRpaStudio\Views\ActivityCreatorView.xaml",
        @"AiRpaStudio\Views\ProjectCreatorView.xaml",
        @"AiRpaStudio\Views\VsProjectOpenView.xaml",
        @"AiRpaStudio\Views\VsTemplateProjectCreatorView.xaml",
        @"AiRpaStudio\Views\ErrorView.xaml",
        @"AiRpaStudio\Views\StartScriptView.xaml"
    };

    // --- Сам контрол заголовка. ---

    [Test]
    public void TitleBar_UsesPaletteOnly()
    {
        string xaml = ReadSource(TitleBarXaml);

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("Studio.TitleBarBackground"),
                "фон полосы заголовка берётся из палитры (как у главного окна), иначе в тёмной теме полоса белая");
            Assert.That(xaml, Does.Contain("Studio.TitleBarForeground"),
                "текст заголовка — Studio.TitleBarForeground");
            Assert.That(xaml, Does.Contain("Studio.CloseHoverBackground"),
                "подсветка кнопки закрытия — красный палитры (Studio.CloseHoverBackground), а не зашитый hex");
            Assert.That(xaml, Does.Contain("Studio.CloseHoverForeground"),
                "иконка закрытия на красной подсветке — Studio.CloseHoverForeground");
            Assert.That(xaml, Does.Not.Match("#[0-9A-Fa-f]{6,8}"), "зашитые цвета запрещены: только ключи палитры");
        });
    }

    [Test]
    public void TitleBar_MatchesMainWindowGeometry()
    {
        string xaml = ReadSource(TitleBarXaml);

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Match(@"Height=""32"""),
                "высота полосы заголовка должна совпадать с главным окном (32)");
            Assert.That(xaml, Does.Match(@"<Setter Property=""Width""\s+Value=""46"""),
                "кнопки заголовка — 46px по ширине, как у главного окна");
            Assert.That(xaml, Does.Match(@"Width=""16"""), "иконка «свернуть» — 16×16");
            Assert.That(xaml, Does.Match(@"Height=""16"""), "иконка «закрыть» — 16×16");
            Assert.That(xaml, Does.Contain("StudioTitleBarButtonStyle"), "кнопки полосы — собственные стили контрола");
            Assert.That(xaml, Does.Contain("Kind=\"WindowMinimize\""), "кнопка «свернуть» на MaterialIcon");
            Assert.That(xaml, Does.Contain("Kind=\"WindowClose\""), "кнопка «закрыть» на MaterialIcon");
            Assert.That(xaml, Does.Contain("MouseLeftButtonDown="),
                "полоса заголовка обязана принимать MouseLeftButtonDown для перетаскивания окна");
        });
    }

    [Test]
    public void TitleBar_DragsAndControlsOwnerWindow()
    {
        string codeBehind = ReadSource(TitleBarCodeBehind);

        Assert.Multiple(() =>
        {
            Assert.That(codeBehind, Does.Contain("Window.GetWindow"),
                "окно-владелец ищется через Window.GetWindow(this): контрол не должен требовать ссылки на окно");
            Assert.That(codeBehind, Does.Contain("DragMove()"),
                "перетаскивание окна — DragMove(), как в MainView");
            Assert.That(codeBehind, Does.Contain("WindowState.Minimized"), "кнопка «свернуть» сворачивает окно");
            Assert.That(codeBehind, Does.Contain("Close()"), "кнопка «закрыть» закрывает окно");
            Assert.That(codeBehind, Does.Contain("WindowState.Maximized"),
                "DragMove нельзя звать в развёрнутом состоянии — WPF бросает InvalidOperationException");
        });
    }

    [Test]
    public void TitleBar_ShowsOwnerTitleAndHidesDisabledButtons()
    {
        var bar = new StudioTitleBar();
        var window = new Window { Title = "Проверка заголовка" };
        window.Content = bar;

        try
        {
            Pump(window);

            var title = (TextBlock)bar.FindName("TitleText");
            var minimize = (Button)bar.FindName("MinimizeButton");
            var close = (Button)bar.FindName("CloseButton");

            Assert.Multiple(() =>
            {
                Assert.That(title, Is.Not.Null, "в полосе заголовка нет TextBlock с именем TitleText");
                Assert.That(title.Text, Is.EqualTo("Проверка заголовка"),
                    "заголовок полосы должен повторять Title окна (у окна ошибки он меняется на «Критическая ошибка»)");
                Assert.That(minimize.Visibility, Is.EqualTo(Visibility.Visible), "по умолчанию «свернуть» видна");
                Assert.That(close.Visibility, Is.EqualTo(Visibility.Visible), "по умолчанию «закрыть» видна");
            });

            bar.ShowMinimize = false;
            Pump(window);

            Assert.That(minimize.Visibility, Is.EqualTo(Visibility.Collapsed),
                "ShowMinimize=false обязано убирать кнопку (окно ошибки не в панели задач)");
            Assert.That(close.Visibility, Is.EqualTo(Visibility.Visible), "кнопка «закрыть» остаётся всегда");

            bar.ShowClose = false;
            Pump(window);

            Assert.That(close.Visibility, Is.EqualTo(Visibility.Collapsed), "ShowClose=false убирает «закрыть»");
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    // --- Все окна студии получают ту же полосу. ---

    [Test]
    public void Dialogs_HaveCustomChromeAndTitleBar()
    {
        foreach (string relativePath in DialogPaths)
        {
            string xaml = ReadSource(relativePath);

            Assert.Multiple(() =>
            {
                Assert.That(xaml, Does.Contain("<controls:StudioTitleBar"),
                    $"{relativePath}: заголовок рисует StudioTitleBar, а не системный caption");
                Assert.That(xaml, Does.Contain("CaptionHeight=\"0\""),
                    $"{relativePath}: CaptionHeight=\"0\" — как у главного окна (системной полосы нет)");
                Assert.That(xaml, Does.Contain("GlassFrameThickness=\"0\""),
                    $"{relativePath}: GlassFrameThickness=\"0\" нужен и для скругления, и для отсутствия стеклянной рамки");
                Assert.That(xaml, Does.Contain("CornerRadius=\"8\""),
                    $"{relativePath}: радиус окна берётся из палитры (StudioPalette.WindowCornerRadius = 8)");
                Assert.That(xaml, Does.Match(@"<RowDefinition\s+Height=""32"""),
                    $"{relativePath}: полоса заголовка занимает строку высотой 32");
            });
        }
    }

    [Test]
    public void Dialogs_InstantiateWithCustomTitleBar()
    {
        // Не только текст XAML: окна реально создаются (InitializeComponent разбирает WindowChrome
        // и StudioTitleBar), и в каждом дереве есть полоса заголовка в строке 0 вместе с chrome.
        var windows = new List<Window>
        {
            new ActivityCreatorView(),
            new ProjectCreatorView(),
            new VsProjectOpenView(),
            new VsTemplateProjectCreatorView(),
            new ErrorView(),
            new StartScriptView()
        };

        try
        {
            foreach (Window window in windows)
            {
                string name = window.GetType().Name;
                var bar = (StudioTitleBar)FindTitleBar(window);

                Assert.Multiple(() =>
                {
                    Assert.That(bar, Is.Not.Null, $"{name}: в окне нет StudioTitleBar — вернулся системный caption");
                    Assert.That(ShellWindowChrome.GetWindowChrome(window), Is.Not.Null,
                        $"{name}: без WindowChrome остаётся системный заголовок и нет region-скругления");
                    Assert.That(bar != null ? Grid.GetRow(bar) : -1, Is.EqualTo(0),
                        $"{name}: полоса заголовка должна стоять в первой строке сетки окна");
                    Assert.That(bar != null ? bar.Height : 0, Is.EqualTo(32d),
                        $"{name}: высота полосы — 32, как у главного окна");
                });
            }
        }
        finally
        {
            foreach (Window window in windows)
            {
                window.Close();
            }
        }
    }

    [Test]
    public void ProjectFile_CompilesTitleBar()
    {
        string csproj = ReadSource(@"AiRpaStudio\AiRpaStudio.csproj");

        Assert.Multiple(() =>
        {
            Assert.That(csproj, Does.Contain(@"Controls\StudioTitleBar.xaml.cs"),
                "code-behind полосы заголовка не добавлен в проект");
            Assert.That(csproj, Does.Contain(@"Controls\StudioTitleBar.xaml"),
                "страница XAML полосы заголовка не добавлена в проект");
        });
    }

    private static FrameworkElement FindTitleBar(DependencyObject root)
    {
        if (root == null)
        {
            return null;
        }

        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is StudioTitleBar bar)
            {
                return bar;
            }

            FrameworkElement found = FindTitleBar(child as DependencyObject);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static void Pump(Window window)
    {
        // Окно показывается вне экрана: RelativeSource FindAncestor разрешается только в
        // визуальном дереве показанного окна, а не при измерении невидимого Window.
        // Окно вне экрана и без активации: тест не перехватывает фокус у рабочего стола.
        if (!window.IsVisible)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10000;
            window.Top = -10000;
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.Show();
        }
        window.UpdateLayout();
    }

    private static string ReadSource(string relativePath)
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
