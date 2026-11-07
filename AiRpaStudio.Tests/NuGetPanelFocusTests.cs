using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NUnit.Framework;
using RPA.NuGet.Services;
using RPA.NuGet.ViewModels;
using RPA.NuGet.Views;

namespace AiRpaStudio.Tests;

/// <summary>
/// Фокус панели NuGet после загрузки.
/// <para>
/// Регрессия из журнала падений: <c>Loaded</c> приходит синхронно из <c>HwndTarget.OnResize</c>
/// (WM_SIZE — разворачивание окна, восстановление раскладки AvalonDock), а синхронный
/// <c>UIElement.Focus()</c> в этот момент падает <c>NullReferenceException</c> в
/// <c>HwndKeyboardInputProvider.AcquireFocus</c> — провайдер ввода ещё не привязан к HwndSource.
/// Поэтому фокус ставится отложенно (DispatcherPriority.Input) и только при живом
/// <c>PresentationSource</c>, а автофокус выполняется один раз — переезд вкладки между панелью
/// документов и боковым доком не должен выдёргивать фокус у пользователя.
/// </para>
/// </summary>
[TestFixture]
public class NuGetPanelFocusTests
{
    [Test]
    public void PanelCodeBehind_FocusIsDeferredToInputPriority()
    {
        string code = Read(@"RPA.NuGet\Views\NuGetPanel.xaml.cs");

        int loaded = code.IndexOf("private void NuGetPanel_Loaded", StringComparison.Ordinal);
        int unloaded = code.IndexOf("private void NuGetPanel_Unloaded", StringComparison.Ordinal);

        Assert.That(loaded, Is.GreaterThan(0), "не найден обработчик Loaded панели");
        Assert.That(unloaded, Is.GreaterThan(loaded), "не найден обработчик Unloaded панели");

        // Границы тела обработчика: от сигнатуры до отложенного вызова включительно.
        int loadedBodyEnd = code.IndexOf("FocusSearchBoxDeferred();", loaded, StringComparison.Ordinal);
        Assert.That(loadedBodyEnd, Is.GreaterThan(loaded), "Loaded обязан ставить фокус отложенно");
        string loadedBody = code.Substring(loaded, loadedBodyEnd - loaded + "FocusSearchBoxDeferred();".Length);

        Assert.Multiple(() =>
        {
            // Источник падения: синхронный Focus() прямо в Loaded.
            Assert.That(loadedBody, Does.Not.Contain(".Focus()"),
                "синхронный Focus() в Loaded падает NRE в HwndKeyboardInputProvider.AcquireFocus");
            Assert.That(loadedBody, Does.Contain("FocusSearchBoxDeferred()"),
                "Loaded обязан ставить фокус отложенно");

            // Отложенный проход идёт на приоритете Input: диспетчер успевает достроить дерево/источник.
            Assert.That(code, Does.Contain("DispatcherPriority.Input"),
                "фокус ставится на DispatcherPriority.Input");
            Assert.That(code, Does.Contain("PresentationSource.FromVisual"),
                "без PresentationSource (нет HwndSource) Focus() не вызывается вовсе");
            Assert.That(code, Does.Contain("IsLoaded"),
                "фокус ставится только у загруженной панели");
            Assert.That(code, Does.Contain("catch (Exception)"),
                "гонка с пересозданием HwndSource не должна валить студию");

            // Ctrl+L идёт через ту же защищённую точку — иначе шорткат повторит падение.
            int shortcut = code.IndexOf("Panel_PreviewKeyDown", StringComparison.Ordinal);
            Assert.That(shortcut, Is.GreaterThan(0), "не найден обработчик горячих клавиш");
            string shortcutBody = code.Substring(shortcut, Math.Min(900, code.Length - shortcut));
            Assert.That(shortcutBody, Does.Contain("TryFocusSearchBox"),
                "Ctrl+L обязан использовать защищённую установку фокуса");
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Panel_LoadedWithoutPresentationSource_DoesNotThrow()
    {
        // Панель, которую загрузили вне окна (источника ввода нет): обработчик не имеет права падать.
        var panel = new NuGetPanel(new NuGetPanelViewModel(new NuGetSearchService("https://127.0.0.1:1/index.json")));

        Assert.DoesNotThrow(() => panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, panel)),
            "Loaded без окна не должен бросать исключение");
        Assert.DoesNotThrow(Pump, "отложенный фокус без HwndSource не должен падать");

        var search = panel.FindName("SearchTextBox") as TextBox;
        Assert.That(search, Is.Not.Null, "нет поля поиска");
        Assert.That(search.IsKeyboardFocused, Is.False, "вне окна фокус клавиатуры не ставится");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Panel_FocusesSearchBox_AfterLoadedInWindow()
    {
        var panel = new NuGetPanel(new NuGetPanelViewModel(new NuGetSearchService("https://127.0.0.1:1/index.json")));
        Window window = ShowInWindow(panel);

        try
        {
            var search = panel.FindName("SearchTextBox") as TextBox;
            Assert.That(search, Is.Not.Null, "нет поля поиска");
            Assert.That(search.IsKeyboardFocused, Is.True,
                "после отложенного прохода фокус обязан быть в поле поиска");
        }
        finally
        {
            window.Close();
        }
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Panel_SecondLoaded_DoesNotStealFocus()
    {
        var panel = new NuGetPanel(new NuGetPanelViewModel(new NuGetSearchService("https://127.0.0.1:1/index.json")));
        Window window = ShowInWindow(panel);

        try
        {
            var search = panel.FindName("SearchTextBox") as TextBox;
            var list = panel.FindName("PackageList") as ListBox;
            Assert.That(search, Is.Not.Null, "нет поля поиска");
            Assert.That(list, Is.Not.Null, "нет списка пакетов");

            // Пользователь ушёл в список, затем панель переехала (Unloaded → Loaded в другом доке).
            Assert.That(list.Focus(), Is.True, "список не принимает фокус");
            Assert.That(search.IsKeyboardFocused, Is.False, "фокус остался в поиске");

            panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, panel));
            panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, panel));
            Pump();

            Assert.That(search.IsKeyboardFocused, Is.False,
                "повторный Loaded не перебивает фокус пользователя (автофокус — только на первом Loaded)");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Хостит панель в окне: без окна нет ни раскладки, ни HwndSource для фокуса.</summary>
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
            Top = -3000
        };

        window.Show();
        panel.UpdateLayout();
        Pump();
        return window;
    }

    /// <summary>Прокручивает очередь диспетчера: отложенный фокус и раскладка идут асинхронно.</summary>
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
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
