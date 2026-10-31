using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using RPA.Chat.Themes;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Этап 8 плана (docs\Vs2026_Theme_Plan.md, §8 п.8; F-08): мост кистей чата.
/// <para>
/// <c>RPA.Chat</c> — отдельная сборка и не может ссылаться на <c>RPA.Helpers</c>, поэтому кисти
/// из палитры студии в его словарь попадают так же, как в окна AvalonDock (этап 3, M-02):
/// по строковым ключам <c>Studio.*</c> во время применения темы. Значения в
/// <c>ChatTheme.Dark.xaml</c>/<c>ChatTheme.Light.xaml</c> — те же нейтральные токены, поэтому чат
/// выглядит одинаково и в студии, и автономно (без ресурсов студии).
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class ChatThemeBridgeTests
{
    [Test]
    public void Mappings_ReferToRealStudioAliases()
    {
        Assert.That(ChatStudioBridge.Mappings, Is.Not.Empty);

        foreach (ChatThemeMapping mapping in ChatStudioBridge.Mappings)
        {
            Assert.Multiple(() =>
            {
                Assert.That(mapping.ChatKey, Is.Not.Empty);
                Assert.That(StudioBrushes.AliasKeys, Does.Contain(mapping.StudioKey),
                    $"{mapping.ChatKey}: ключ {mapping.StudioKey} не объявлен в StudioBrushes — опечатка сделает мост пустым");
            });
        }

        Assert.That(
            ChatStudioBridge.Mappings.Select(m => m.ChatKey).Distinct(StringComparer.Ordinal).Count(),
            Is.EqualTo(ChatStudioBridge.Mappings.Count),
            "ключи чата в таблице моста должны быть уникальны");
    }

    [Test]
    public void Apply_OverridesChatDictionaryFromPalette()
    {
        foreach (bool dark in new[] { true, false })
        {
            ResourceDictionary studio = StudioResources(dark);
            ResourceDictionary chat = ParseDictionary(ReadTheme(dark));

            ChatStudioBridge.Apply(chat, studio);

            foreach (ChatThemeMapping mapping in ChatStudioBridge.Mappings)
            {
                Color expected = ColorOf(studio[mapping.StudioKey]);
                if (mapping.Alpha != 0)
                {
                    expected = Color.FromArgb(mapping.Alpha, expected.R, expected.G, expected.B);
                }

                Assert.That(ColorOf(chat[mapping.ChatKey]), Is.EqualTo(expected),
                    $"{(dark ? "dark" : "light")}: {mapping.ChatKey} должен прийти из {mapping.StudioKey}");
            }
        }
    }

    [Test]
    public void Apply_WithoutStudioResources_KeepsDictionaryValues()
    {
        ResourceDictionary chat = ParseDictionary(ReadTheme(dark: true));
        Color before = ColorOf(chat["Dark.Accent"]);

        ChatStudioBridge.Apply(chat, null);

        Assert.That(ColorOf(chat["Dark.Accent"]), Is.EqualTo(before),
            "без ресурсов студии мост обязан молча ничего не менять");
    }

    [Test]
    public void ThemeDictionaries_AlreadyMatchPalette()
    {
        // Автономный режим: значения словаря совпадают с палитрой, поэтому чат вне студии выглядит так же.
        foreach (bool dark in new[] { true, false })
        {
            ResourceDictionary studio = StudioResources(dark);
            ResourceDictionary chat = ParseDictionary(ReadTheme(dark));

            foreach (ChatThemeMapping mapping in ChatStudioBridge.Mappings)
            {
                Color expected = ColorOf(studio[mapping.StudioKey]);
                if (mapping.Alpha != 0)
                {
                    expected = Color.FromArgb(mapping.Alpha, expected.R, expected.G, expected.B);
                }

                Assert.That(chat.Contains(mapping.ChatKey), Is.True,
                    $"{(dark ? "dark" : "light")}: в словаре чата нет ключа {mapping.ChatKey}");
                Assert.That(ColorOf(chat[mapping.ChatKey]), Is.EqualTo(expected),
                    $"{(dark ? "dark" : "light")}: {mapping.ChatKey} расходится с палитрой — правьте словарь и таблицу моста вместе");
            }
        }
    }

    [Test]
    public void Resolve_FallsBackWithoutStudioResources()
    {
        Brush fallback = Brushes.Gray;

        Assert.Multiple(() =>
        {
            Assert.That(ChatStudioBridge.Resolve(null, StudioBrushes.SelectedKey, fallback), Is.SameAs(fallback));
            Assert.That(ChatStudioBridge.Resolve(new ResourceDictionary(), StudioBrushes.SelectedKey, fallback), Is.SameAs(fallback));
        });
    }

    [Test]
    public void Resolve_ReturnsStudioBrush()
    {
        ResourceDictionary studio = StudioResources(dark: true);

        Brush brush = ChatStudioBridge.Resolve(studio, StudioBrushes.SelectedKey, Brushes.Gray);

        Assert.That(ColorOf(brush), Is.EqualTo(StudioPalette.Dark.Selected));
    }

    private static ResourceDictionary StudioResources(bool dark)
    {
        ResourceDictionary studio = new();
        StudioBrushes.ApplyPalette(studio, StudioPalette.For(dark));
        return studio;
    }

    private static string ReadTheme(bool dark)
    {
        string path = Path.Combine(FindRepositoryRoot(), "RPA.Chat", "Themes",
            dark ? "ChatTheme.Dark.xaml" : "ChatTheme.Light.xaml");

        Assert.That(File.Exists(path), Is.True, $"не найден {path}");
        return File.ReadAllText(path);
    }

    private static ResourceDictionary ParseDictionary(string xaml)
    {
        return (ResourceDictionary)XamlReader.Parse(xaml);
    }

    private static Color ColorOf(object value)
    {
        Assert.That(value, Is.InstanceOf<SolidColorBrush>(), "ожидалась сплошная кисть");
        return ((SolidColorBrush)value).Color;
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
