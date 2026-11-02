using System;
using System.IO;
using System.Windows;
using AvalonDock.Themes;
using RPA.Helpers;
using ShellWindowChrome = System.Windows.Shell.WindowChrome;

namespace AiRpaStudio.Tests;

/// <summary>
/// Этап 5 плана (docs\Vs2026_Theme_Plan.md, §7 M-03, W-01…W-08): скруглённые окна.
/// Основной механизм — DWM (<c>DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, Round)</c>,
/// Windows 11); fallback на Windows 10 — region через <c>WindowChrome.CornerRadius</c>.
/// Окна AvalonDock (откреплённые панели, Ctrl+Tab) не могут ссылаться на сборку студии, поэтому для них
/// есть мост <see cref="WindowCornerBridge"/> — зеркально мосту кистей этапа 3.
/// </summary>
[Apartment(ApartmentState.STA)]
public class WindowCornerTests
{
    // --- Константы DWM: контракт с dwmapi.dll. ---

    [Test]
    public void Constants_MatchDwmContract()
    {
        Assert.That(WindowCorner.DwmWindowCornerPreference, Is.EqualTo(33)); // DWMWA_WINDOW_CORNER_PREFERENCE
        Assert.That(WindowCorner.DwmWindowCornerDefault, Is.EqualTo(0));
        Assert.That(WindowCorner.DwmWindowCornerDoNotRound, Is.EqualTo(1));
        Assert.That(WindowCorner.DwmWindowCornerRound, Is.EqualTo(2));
    }

    [Test]
    public void CornerRadius_ComesFromPalette()
    {
        // Радиус окна не дублируется в хелпере: единственный источник — палитра (правило 3 §1).
        Assert.That(StudioPalette.WindowCornerRadius, Is.EqualTo(8d));
    }

    // --- Apply: хелпер декоративный, он не имеет права бросать исключения. ---

    [Test]
    public void Apply_ZeroHandle_ReturnsFalseWithoutThrowing()
    {
        Assert.That(WindowCorner.Apply(IntPtr.Zero), Is.False);
    }

    [Test]
    public void Apply_InvalidHandle_ReturnsFalseWithoutThrowing()
    {
        // На невалидном HWND dwmapi возвращает ненулевой HRESULT; исключение наружу не выходит.
        Assert.That(WindowCorner.Apply(new IntPtr(0xDEAD)), Is.False);
    }

    [Test]
    public void Apply_NullWindow_ReturnsFalse()
    {
        Assert.That(WindowCorner.Apply((Window)null), Is.False);
    }

    [Test]
    public void Apply_WindowWithoutHandle_ReturnsFalse()
    {
        var window = new Window();
        Assert.That(WindowCorner.Apply(window), Is.False);
        window.Close();
    }

    // --- Enable / IsEnabled: идемпотентность включения. ---

    [Test]
    public void Enable_NullWindow_DoesNothing()
    {
        Assert.DoesNotThrow(() => WindowCorner.Enable(null));
        Assert.That(WindowCorner.IsEnabled(null), Is.False);
        Assert.That(WindowCorner.ActivationCount(null), Is.EqualTo(0));
    }

    [Test]
    public void IsEnabled_FalseForUnknownWindow()
    {
        var window = new Window();

        Assert.That(WindowCorner.IsEnabled(window), Is.False);
        Assert.That(WindowCorner.ActivationCount(window), Is.EqualTo(0));

        window.Close();
    }

    [Test]
    public void Enable_IsIdempotent()
    {
        var window = new Window();

        WindowCorner.Enable(window);
        WindowCorner.Enable(window);
        WindowCorner.Enable(window);

        Assert.That(WindowCorner.IsEnabled(window), Is.True);
        Assert.That(WindowCorner.ActivationCount(window), Is.EqualTo(1),
            "повторный Enable не должен подписывать окно второй раз");

        window.Close();
    }

    [Test]
    public void Enable_IsolatesWindows()
    {
        var first = new Window();
        var second = new Window();

        WindowCorner.Enable(first);

        Assert.That(WindowCorner.ActivationCount(first), Is.EqualTo(1));
        Assert.That(WindowCorner.ActivationCount(second), Is.EqualTo(0));
        Assert.That(WindowCorner.IsEnabled(second), Is.False);

        first.Close();
        second.Close();
    }

    // --- Fallback Windows 10: region через WindowChrome.CornerRadius. ---

    [Test]
    public void ApplyCornerRadiusFallback_WithoutChrome_ReturnsFalseAndAddsNothing()
    {
        var window = new Window();

        Assert.That(WindowCorner.ApplyCornerRadiusFallback(window), Is.False);
        Assert.That(ShellWindowChrome.GetWindowChrome(window), Is.Null,
            "окну без WindowChrome нельзя навязывать chrome: слетит системный заголовок диалога");

        window.Close();
    }

    [Test]
    public void ApplyCornerRadiusFallback_NullWindow_ReturnsFalse()
    {
        Assert.That(WindowCorner.ApplyCornerRadiusFallback(null), Is.False);
    }

    [Test]
    public void ApplyCornerRadiusFallback_SetsPaletteRadius()
    {
        var window = new Window();
        var chrome = new ShellWindowChrome { CaptionHeight = 0 };
        ShellWindowChrome.SetWindowChrome(window, chrome);

        Assert.That(WindowCorner.ApplyCornerRadiusFallback(window), Is.True);
        Assert.That(chrome.CornerRadius.TopLeft, Is.EqualTo(StudioPalette.WindowCornerRadius));
        Assert.That(chrome.CornerRadius.BottomRight, Is.EqualTo(StudioPalette.WindowCornerRadius));

        window.Close();
    }

    // --- Мост в ядро AvalonDock. ---

    [Test]
    public void Bridge_WithoutProvider_ReturnsFalse()
    {
        WindowCornerBridge.Disconnect();

        Assert.That(WindowCornerBridge.Apply(new IntPtr(0x1234)), Is.False);
        Assert.That(WindowCornerBridge.IsConnected, Is.False);
    }

    [Test]
    public void Bridge_ZeroHandle_DoesNotCallProvider()
    {
        bool called = false;
        WindowCornerBridge.Provider = _ =>
        {
            called = true;
            return true;
        };

        try
        {
            Assert.That(WindowCornerBridge.Apply(IntPtr.Zero), Is.False);
            Assert.That(called, Is.False);
        }
        finally
        {
            WindowCornerBridge.Disconnect();
        }
    }

    [Test]
    public void Bridge_DelegatesToProvider()
    {
        WindowCornerBridge.Provider = _ => true;

        try
        {
            Assert.That(WindowCornerBridge.IsConnected, Is.True);
            Assert.That(WindowCornerBridge.Apply(new IntPtr(0x1234)), Is.True);
        }
        finally
        {
            WindowCornerBridge.Disconnect();
        }

        Assert.That(WindowCornerBridge.Apply(new IntPtr(0x1234)), Is.False);
    }

    [Test]
    public void Bridge_ProviderThrows_ReturnsFalse()
    {
        WindowCornerBridge.Provider = _ => throw new InvalidOperationException("dwmapi упал");

        try
        {
            Assert.That(WindowCornerBridge.Apply(new IntPtr(0x1234)), Is.False);
        }
        finally
        {
            WindowCornerBridge.Disconnect();
        }
    }

    [Test]
    public void Bridge_CoreFile_HasNoStudioDependency()
    {
        // Ядро AvalonDock не ссылается на сборку студии — мост обязан оставаться на Func<IntPtr,bool>.
        string source = ReadSource(Path.Combine("AvalonDock", "Themes", "WindowCornerBridge.cs"));

        Assert.That(source, Does.Not.Contain("RPA."));
        Assert.That(source, Does.Not.Contain("StudioPalette"));
    }

    // --- Стражи по исходникам: точки подключения не должны потеряться при рефакторинге. ---

    [Test]
    public void MainWindow_UsesPaletteRadius()
    {
        string xaml = ReadSource(Path.Combine("AiRpaStudio", "Views", "MainView.xaml"));

        Assert.That(xaml, Does.Contain($"CornerRadius=\"{StudioPalette.WindowCornerRadius}\""),
            "главное окно должно скругляться радиусом палитры");
        Assert.That(xaml, Does.Contain("GlassFrameThickness=\"0\""),
            "region-путь скругления работает только без glass-frame");
    }

    [Test]
    public void StudioWindows_EnableWindowCorner()
    {
        foreach (string name in new[]
                 {
                     "MainView.xaml.cs",
                     "ActivityCreatorView.xaml.cs",
                     "ProjectCreatorView.xaml.cs",
                     "VsProjectOpenView.xaml.cs",
                     "VsTemplateProjectCreatorView.xaml.cs",
                     "ErrorView.xaml.cs"
                 })
        {
            string source = ReadSource(Path.Combine("AiRpaStudio", "Views", name));

            Assert.That(source, Does.Contain("WindowCorner.Enable(this)"), $"{name}: скругление не подключено");
            Assert.That(source, Does.Contain("OnSourceInitialized"), $"{name}: нет override OnSourceInitialized");
        }
    }

    [Test]
    public void AvalonDockWindows_UseCornerBridge()
    {
        foreach (string name in new[] { "LayoutFloatingWindowControl.cs", "NavigatorWindow.cs" })
        {
            string source = ReadSource(Path.Combine("AvalonDock", "Controls", name));

            Assert.That(source, Does.Contain("WindowCornerBridge.Apply"), $"{name}: мост не вызывается");
            Assert.That(source, Does.Contain("protected override void OnSourceInitialized"),
                $"{name}: HWND доступен только после OnSourceInitialized");
        }
    }

    [Test]
    public void AppStartup_ConnectsCornerBridge()
    {
        string source = ReadSource(Path.Combine("AiRpaStudio", "App.xaml.cs"));

        Assert.That(source, Does.Contain("WindowCorner.EnableAvalonDockWindows()"),
            "мост скругления должен подключаться на старте приложения");
    }

    [Test]
    public void ProjectFile_CompilesHelper()
    {
        string csproj = ReadSource(Path.Combine("AiRpaStudio", "AiRpaStudio.csproj"));

        Assert.That(csproj, Does.Contain("Helpers\\WindowCorner.cs"));
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
