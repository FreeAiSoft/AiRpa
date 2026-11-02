using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Этап 12 плана (docs\Vs2026_Theme_Plan.md): шрифт Roboto для всего проекта.
/// <para>
/// Правила, которые охраняют тесты: шрифты объявляются только в
/// <see cref="StudioFonts"/>; в XAML — только <c>{DynamicResource Studio.FontFamily}</c>
/// (<c>Studio.CodeFontFamily</c> для редакторов кода); наборы Consolas/Segoe UI не возвращаются
/// ни в студию, ни в лог, ни в чат, ни в тему AvalonDock.
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class StudioFontTests
{
    [Test]
    public void FamilyName_IsRoboto()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StudioFonts.FamilyName, Is.EqualTo("Roboto"));
            Assert.That(StudioFonts.CodeFamilyName, Is.EqualTo(StudioFonts.FamilyName), "шрифт кода — тот же Roboto (одна точка правки)");
            Assert.That(StudioFonts.UiFont.Source, Does.Contain("Roboto"));
            Assert.That(StudioFonts.CodeFont.Source, Does.Contain("Roboto"));
        });
    }

    [Test]
    public void Apply_WritesAllKeys()
    {
        var target = new ResourceDictionary();

        StudioFonts.Apply(target);

        Assert.Multiple(() =>
        {
            Assert.That(target[StudioFonts.FontFamilyKey], Is.SameAs(StudioFonts.UiFont));
            Assert.That(target[StudioFonts.CodeFontFamilyKey], Is.SameAs(StudioFonts.CodeFont));
            Assert.That(target[StudioFonts.FontFamilyNameKey], Is.EqualTo(StudioFonts.FamilyName));
            Assert.That(StudioFonts.ResourceKeys, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void Apply_IsRepeatable_AndNullSafe()
    {
        var target = new ResourceDictionary();

        StudioFonts.Apply(target);
        object first = target[StudioFonts.FontFamilyKey];
        StudioFonts.Apply(target);

        Assert.Multiple(() =>
        {
            Assert.That(target[StudioFonts.FontFamilyKey], Is.SameAs(first), "повторное применение не пересоздаёт шрифт");
            Assert.DoesNotThrow(() => StudioFonts.Apply(null));
            Assert.DoesNotThrow(() => StudioFonts.ApplyToApplication());
        });
    }

    [Test]
    public void Resolve_FallsBackWhenKeyIsMissing()
    {
        var resources = new ResourceDictionary();
        FontFamily fallback = StudioFonts.UiFont;

        Assert.That(StudioFonts.Resolve(resources, StudioFonts.FontFamilyKey, fallback), Is.SameAs(fallback));
        Assert.That(StudioFonts.Resolve(null, StudioFonts.FontFamilyKey, fallback), Is.SameAs(fallback));

        StudioFonts.Apply(resources);
        Assert.That(StudioFonts.Resolve(resources, StudioFonts.CodeFontFamilyKey, fallback), Is.SameAs(StudioFonts.CodeFont));
    }

    [Test]
    public void RobotoTypefaces_AreEmbeddedIntoAssembly()
    {
        // Шрифт должен ехать вместе со студией: на машине пользователя Roboto может быть не установлен.
        Assembly assembly = typeof(StudioFonts).Assembly;
        string resources = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase));

        Assert.That(resources, Is.Not.Null, "в сборке студии нет таблицы ресурсов *.g.resources");

        using Stream stream = assembly.GetManifestResourceStream(resources);

        var names = new List<string>();
        using (var reader = new System.Resources.ResourceReader(stream))
        {
            foreach (DictionaryEntry entry in reader)
            {
                names.Add(Convert.ToString(entry.Key));
            }
        }

        Assert.That(
            names.Any(name => name.IndexOf("roboto-regular.ttf", StringComparison.OrdinalIgnoreCase) >= 0),
            Is.True,
            "встроенные начертания Roboto не найдены в ресурсах сборки");
    }

    // --- Стражи по исходникам: чужие шрифты не возвращаются. ---

    [Test]
    public void StudioXaml_UsesOnlyStudioFontResources()
    {
        foreach (string file in XamlFiles())
        {
            string text = File.ReadAllText(file);
            string relative = Relative(file);

            foreach (Match match in Regex.Matches(text, "FontFamily=\"([^\"]*)\"", RegexOptions.None, TimeSpan.FromSeconds(2)))
            {
                string value = match.Groups[1].Value;

                Assert.That(value.StartsWith("{DynamicResource", StringComparison.Ordinal)
                            || value.StartsWith("{Binding", StringComparison.Ordinal)
                            || value.StartsWith("{StaticResource", StringComparison.Ordinal)
                            || value.StartsWith("{TemplateBinding", StringComparison.Ordinal),
                    Is.True,
                    $"{relative}: шрифт задаётся только ссылкой на ресурс студии, а не строкой \"{value}\"");
            }
        }
    }

    [Test]
    public void StudioSources_DoNotCreateForeignFonts()
    {
        Regex forbidden = new Regex("new\\s+(System\\.Windows\\.Media\\.)?FontFamily\\(\"(Consolas|Segoe UI|SegoeUI)\"",
            RegexOptions.None, TimeSpan.FromSeconds(2));

        foreach (string file in SourceFiles())
        {
            string text = File.ReadAllText(file);
            Assert.That(forbidden.IsMatch(text), Is.False,
                $"{Relative(file)}: шрифт берётся только из StudioFonts.UiFont/CodeFont");
        }
    }

    [Test]
    public void ThemeXaml_UsesStudioFont()
    {
        string text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "AvalonDock.Themes.Metro", "Theme.xaml"));

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("{DynamicResource Studio.FontFamily}"),
                "тема AvalonDock обязана брать шрифт из ресурсов студии");
            Assert.That(text, Does.Not.Contain("\"Segoe UI\""), "Segoe UI в теме больше не используется");
            Assert.That(Regex.IsMatch(text, "Value=\"Roboto\""), Is.False,
                "жёсткая строка Roboto резолвится только для установленного шрифта — нужен ресурс студии");
        });
    }

    [Test]
    public void ProjectFile_CompilesStudioFonts()
    {
        string csproj = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "AiRpaStudio", "AiRpaStudio.csproj"));

        Assert.That(csproj, Does.Contain("Helpers\\StudioFonts.cs"));
        Assert.That(csproj, Does.Contain("Resources\\Roboto\\Roboto-Regular.ttf"), "начертания Roboto остаются встроенными");
    }

    [Test]
    public void StartupAndTheme_WireStudioFonts()
    {
        string app = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "AiRpaStudio", "App.xaml.cs"));
        string settings = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "AiRpaStudio", "Models", "AppSettings.cs"));
        string chat = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "RPA.Chat", "Themes", "ChatFonts.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(app, Does.Contain("StudioFonts.ApplyToApplication()"), "шрифты пишутся на старте приложения");
            Assert.That(settings, Does.Contain("StudioFonts.ApplyToApplication()"), "и после каждой смены темы");
            Assert.That(chat, Does.Contain(StudioFonts.FontFamilyKey), "чат читает шрифт студии по ключу");
        });
    }

    private static IEnumerable<string> XamlFiles()
    {
        foreach (string directory in new[] { "AiRpaStudio", "LogControlLibrary", "RPA.Chat", "RPA.NuGet", "AvalonDock.Themes.Metro" })
        {
            string root = Path.Combine(FindRepositoryRoot(), directory);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string file in Directory.GetFiles(root, "*.xaml", SearchOption.AllDirectories))
            {
                if (!IsGenerated(file) && !IsTemplate(file))
                {
                    yield return file;
                }
            }
        }
    }

    private static IEnumerable<string> SourceFiles()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(FindRepositoryRoot(), "AiRpaStudio"), "*.cs", SearchOption.AllDirectories))
        {
            if (!IsGenerated(file))
            {
                yield return file;
            }
        }
    }

    /// <summary>Сгенерированные файлы (<c>obj</c>/<c>bin</c>/<c>*.g.cs</c>) не проверяем.</summary>
    private static bool IsGenerated(string file)
    {
        return file.IndexOf(@"\obj\", StringComparison.OrdinalIgnoreCase) >= 0
            || file.IndexOf(@"\bin\", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Шаблоны создаваемых пользователем проектов — не интерфейс студии.</summary>
    private static bool IsTemplate(string file)
    {
        return file.IndexOf(@"\VSTemplates\", StringComparison.OrdinalIgnoreCase) >= 0
            || file.IndexOf(@"\TestProcess\", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string Relative(string file)
    {
        return file.Replace(FindRepositoryRoot() + Path.DirectorySeparatorChar, string.Empty);
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
