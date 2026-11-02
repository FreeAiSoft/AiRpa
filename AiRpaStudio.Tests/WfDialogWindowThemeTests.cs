using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RPA.Controls;
using RPA.Helpers;
using ShellWindowChrome = System.Windows.Shell.WindowChrome;

namespace AiRpaStudio.Tests;

/// <summary>
/// Баг «у редактора выражений в тёмной теме белая шапка»: редактор, который открывается кнопкой «…»
/// в окне «Свойства», живёт в окне WF (<c>WorkflowElementDialogWindow</c>/<c>EditorDialog</c>) — это
/// обычный <see cref="Window"/> с системным заголовком Windows (полоса с «свернуть/развернуть/закрыть»),
/// белым в любой теме. <see cref="WfDesignerTheme"/> до него не достаёт: окно не является потомком
/// <c>DesignerView</c>.
/// </summary>
/// <para>
/// Проверяется <see cref="WfDialogWindowTheme"/>: системный caption снимается
/// (<c>WindowChrome.CaptionHeight = 0</c>), цвета берутся из палитры ключами (DynamicResource —
/// переживает смену темы), содержимое оборачивается в полосу заголовка <see cref="StudioTitleBar"/>
/// 32 px, скругление включается тем же <see cref="WindowCorner"/>, что и у остальных окон.
/// </para>
[Apartment(ApartmentState.STA)]
public class WfDialogWindowThemeTests
{
    [Test]
    public void Apply_NullWindow_ReturnsFalse()
    {
        Assert.That(WfDialogWindowTheme.Apply(null), Is.False);
    }

    [Test]
    public void Apply_RemovesSystemCaption()
    {
        var window = new System.Activities.Presentation.WorkflowElementDialogWindow { Title = "Редактор выражений" };
        window.Content = new Border();

        try
        {
            Assert.That(WfDialogWindowTheme.Apply(window), Is.True);

            ShellWindowChrome chrome = ShellWindowChrome.GetWindowChrome(window);
            Assert.Multiple(() =>
            {
                Assert.That(chrome, Is.Not.Null, "без WindowChrome системный caption остаётся белым");
                Assert.That(chrome.CaptionHeight, Is.EqualTo(0d),
                    "CaptionHeight = 0 убирает системную полосу с кнопками свернуть/закрыть");
                Assert.That(chrome.GlassFrameThickness, Is.EqualTo(new Thickness(0)));
                Assert.That(chrome.CornerRadius, Is.EqualTo(new CornerRadius(StudioPalette.WindowCornerRadius)),
                    "радиус окна — из палитры, как у остальных окон студии");
                Assert.That(chrome.ResizeBorderThickness.Left, Is.EqualTo(WfDialogWindowTheme.ResizeBorderThickness),
                    "вместо системного grip окно меняет размер за кромку");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Apply_DrawsStudioTitleBarAndKeepsContent()
    {
        var window = new System.Activities.Presentation.WorkflowElementDialogWindow { Title = "Редактор выражений" };
        var originalContent = new Border { Name = "wfContent" };
        window.Content = originalContent;

        try
        {
            Assert.That(WfDialogWindowTheme.Apply(window), Is.True);

            var root = window.Content as Grid;
            Assert.That(root, Is.Not.Null, "содержимое окна оборачивается в Grid");

            StudioTitleBar titleBar = root.Children.OfType<StudioTitleBar>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(Grid.GetRow(titleBar), Is.EqualTo(0));
                Assert.That(root.RowDefinitions[0].Height.Value, Is.EqualTo(WfDialogWindowTheme.TitleBarHeight),
                    "полоса заголовка — 32 px, как у главного окна и диалогов студии");
                Assert.That(root.Children.OfType<Border>().Single(), Is.SameAs(originalContent),
                    "содержимое WF-диалога должно остаться на месте");
                Assert.That(Grid.GetRow(originalContent), Is.EqualTo(1));
                Assert.That(root.RowDefinitions[1].Height.IsStar, Is.True, "содержимое занимает всё остальное место");
                Assert.That(window.Title, Is.EqualTo("Редактор выражений"), "заголовок окна не теряется");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Apply_IsIdempotent()
    {
        var window = new System.Activities.Presentation.WorkflowElementDialogWindow();
        window.Content = new Border();

        try
        {
            Assert.That(WfDialogWindowTheme.Apply(window), Is.True);
            Assert.That(WfDialogWindowTheme.Apply(window), Is.False, "повторный вызов ничего не делает");
            Assert.That(WfDialogWindowTheme.IsApplied(window), Is.True);

            var root = (Grid)window.Content;
            Assert.That(root.Children.OfType<StudioTitleBar>().Count(), Is.EqualTo(1),
                "полоса заголовка не должна дублироваться");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Apply_WithEmptyContent_StillAddsTitleBar()
    {
        var window = new System.Activities.Presentation.WorkflowElementDialogWindow();
        window.Content = null;

        try
        {
            Assert.That(WfDialogWindowTheme.Apply(window), Is.True);

            var root = window.Content as Grid;
            Assert.That(root, Is.Not.Null);
            Assert.That(root.Children.OfType<StudioTitleBar>().Count(), Is.EqualTo(1));
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Apply_SetsPaletteResources()
    {
        var window = new System.Activities.Presentation.WorkflowElementDialogWindow();
        window.Content = new Border();

        try
        {
            WfDialogWindowTheme.Apply(window);

            Assert.Multiple(() =>
            {
                // SetResourceReference кладёт отложенную ссылку: значение переживает смену темы.
                Assert.That(window.ReadLocalValue(Control.BackgroundProperty), Is.Not.EqualTo(DependencyProperty.UnsetValue),
                    "фон окна должен браться из палитры (Studio.Surface), а не из системного белого");
                Assert.That(window.ReadLocalValue(Control.ForegroundProperty), Is.Not.EqualTo(DependencyProperty.UnsetValue),
                    "текст окна — Studio.Foreground");
                Assert.That(window.ReadLocalValue(Control.FontFamilyProperty), Is.Not.EqualTo(DependencyProperty.UnsetValue),
                    "шрифт окна — Studio.FontFamily (Roboto)");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void IsWfDialog_RecognizesWfWindowsOnly()
    {
        var wfDialog = new System.Activities.Presentation.WorkflowElementDialogWindow();
        var studioWindow = new Window();

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(WfDialogWindowTheme.IsWfDialog(wfDialog), Is.True);
                Assert.That(WfDialogWindowTheme.IsWfDialog(studioWindow), Is.False,
                    "окна студии уже на теме студии — их трогать нельзя");
                Assert.That(WfDialogWindowTheme.IsWfDialog(null), Is.False);
            });
        }
        finally
        {
            wfDialog.Close();
            studioWindow.Close();
        }
    }

    [Test]
    public void ExpressionEditor_AppliesThemeToWfDialog()
    {
        // Точка вызова: окно редактора выражений находится при загрузке контрола (там же, где
        // восстанавливается размер окна) — иначе тема к нему не попадёт.
        string source = ReadSource(@"AiRpaStudio\CSharpExpressionEditor\RoslynExpressionEditor.xaml.cs");

        Assert.That(source, Does.Contain("WfDialogWindowTheme.Apply(parentWindow)"),
            "редактор выражений обязан переносить тему на окно WF");
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
