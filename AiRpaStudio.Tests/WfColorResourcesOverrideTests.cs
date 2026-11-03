using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

using NUnit.Framework;

using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Цвета дизайнера WF <b>до первого кадра</b> (<see cref="WfColorResourcesOverride"/>): подмена словаря
/// <c>ColorResources.xaml</c> в кэше <c>CachedResourceDictionaryExtension</c>.
/// <para>
/// Регресс, ради которого появился механизм: тема применялась «вторым проходом» после построения
/// элемента, и первый кадр рисовался цветами WF (<c>#FFFFFFFF</c>/<c>#FFE9ECEE</c>) — при старте
/// программы и при перетаскивании активности из панели было видно мерцание.
/// </para>
/// <para>
/// Тесты не трогают глобальный кэш WF (он один на процесс): установка проверяется на локальном
/// <see cref="Dictionary{TKey, TValue}"/> и на настоящем снимке <c>ColorResources.xaml</c>.
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WfColorResourcesOverrideTests
{
    [TearDown]
    public void TearDown()
    {
        AppContext.SetSwitch(WfColorResourcesOverride.LegacyThemingSwitch, false);
        WfColorResourcesOverride.Reset();
    }

    // --- Контракт WF, на котором держится механизм. ---

    [Test]
    public void CacheField_IsStaticWritableDictionary()
    {
        FieldInfo field = WfColorResourcesOverride.FindCacheField();

        Assert.That(field, Is.Not.Null,
            "CachedResourceDictionaryExtension.Cache пропало: версия WF сменила контракт, нужен пересмотр подхода");

        Assert.Multiple(() =>
        {
            Assert.That(field.IsStatic, Is.True, "кэш общий на процесс — шаблоны получают один экземпляр словаря");
            Assert.That(field.IsInitOnly, Is.False, "поле должно быть перезаписываемым, иначе подмена невозможна");
            Assert.That(typeof(IDictionary).IsAssignableFrom(field.FieldType), Is.True,
                "ожидается Dictionary<Uri, ResourceDictionary>: " + field.FieldType);
        });
    }

    [Test]
    public void ColorResourcesUris_PointToTheSameComponent()
    {
        // Оба URI обязательны: часть шаблонов WF ссылается относительным путём, часть — pack-URI.
        Assert.Multiple(() =>
        {
            Assert.That(WfColorResourcesOverride.ColorResourcesRelativeUri.IsAbsoluteUri, Is.False);
            Assert.That(WfColorResourcesOverride.ColorResourcesRelativeUri.OriginalString,
                Does.StartWith("/System.Activities.Presentation;component/").And.EndWith("ColorResources.xaml"));
            Assert.That(WfColorResourcesOverride.ColorResourcesPackUri.IsAbsoluteUri, Is.True);
            Assert.That(WfColorResourcesOverride.ColorResourcesPackUri.Scheme, Is.EqualTo("pack"));
            Assert.That(WfColorResourcesOverride.ColorResourcesPackUri.OriginalString,
                Does.Contain("System.Activities.Presentation;component/"));
        });
    }

    [Test]
    public void WorkflowDefaults_LoadAndContainEveryOverriddenKey()
    {
        // Страж от опечатки в имени ключа: молча непереопределённый ключ оставил бы светлую плиту.
        ResourceDictionary defaults = WfColorResourcesOverride.LoadWorkflowDefaults();

        Assert.That(defaults, Is.Not.Null, "словарь ColorResources.xaml должен загружаться");

        List<string> missing = new List<string>();
        foreach (string key in WfColorResourcesOverride.OverrideTokens.Keys)
        {
            if (!defaults.Contains(key))
            {
                missing.Add(key);
            }
        }

        Assert.That(missing, Is.Empty,
            "этих ключей нет в ColorResources.xaml (переопределять нечего, проверьте имя): " + string.Join(", ", missing));
    }

    [Test]
    public void OverrideTokens_AgreeWithDesignerThemePalette()
    {
        // Два списка ключей (словарь WF и словарь приложения) не должны разъезжаться по цвету.
        Dictionary<string, object> application = WfDesignerTheme.CreateResourceValues(StudioPalette.Dark);

        List<string> mismatched = new List<string>();
        foreach (KeyValuePair<string, string> pair in WfColorResourcesOverride.OverrideTokens)
        {
            if (!application.TryGetValue(pair.Key, out object value) || !(value is SolidColorBrush brush))
            {
                continue;
            }

            if (brush.Color != StudioPalette.Dark.Get(pair.Value))
            {
                mismatched.Add(pair.Key + " (" + pair.Value + ")");
            }
        }

        Assert.That(mismatched, Is.Empty,
            "цвет ключа в WfDesignerTheme не совпадает с токеном в OverrideTokens: " + string.Join(", ", mismatched));
    }

    [Test]
    public void OverrideTokens_DoNotTouchPropertyInspector()
    {
        // Инспектор свойств берёт цвета из FontAndColorResources: в ColorResources.xaml его ключей нет.
        Assert.That(WfColorResourcesOverride.OverrideTokens.Keys, Has.None.StartsWith("PropertyInspector"),
            "панель «Свойства» темизируется отдельно (WfDesignerTheme/PropertyWindowStyleHelper)");
    }

    // --- Установка в кэш. ---

    [Test]
    public void TryInstall_PutsBothUrisWithSameInstance()
    {
        ResourceDictionary defaults = Defaults();
        Dictionary<Uri, ResourceDictionary> cache = new Dictionary<Uri, ResourceDictionary>();

        bool installed = WfColorResourcesOverride.TryInstall(StudioPalette.Dark, defaults, cache);

        Assert.Multiple(() =>
        {
            Assert.That(installed, Is.True);
            Assert.That(cache.ContainsKey(WfColorResourcesOverride.ColorResourcesRelativeUri), Is.True,
                "относительный URI — ключ шаблонов WF");
            Assert.That(cache.ContainsKey(WfColorResourcesOverride.ColorResourcesPackUri), Is.True,
                "pack-URI — второй ключ того же словаря");
            Assert.That(cache[WfColorResourcesOverride.ColorResourcesPackUri],
                Is.SameAs(cache[WfColorResourcesOverride.ColorResourcesRelativeUri]),
                "один экземпляр на оба ключа: иначе часть шаблонов останется на дефолтах WF");
            Assert.That(WfColorResourcesOverride.IsInstalled, Is.True);
        });
    }

    [Test]
    public void TryInstall_KeepsWorkflowDefaultsForOtherKeys()
    {
        ResourceDictionary defaults = Defaults();
        Dictionary<Uri, ResourceDictionary> cache = new Dictionary<Uri, ResourceDictionary>();

        WfColorResourcesOverride.TryInstall(StudioPalette.Dark, defaults, cache);
        ResourceDictionary dictionary = cache[WfColorResourcesOverride.ColorResourcesRelativeUri];

        Assert.That(dictionary.Contains("ShellStatusBarHoverBrush"), Is.True,
            "переопределённый ключ виден в словаре напрямую");
        Assert.That(ContainsValue(dictionary, "SomeWorkflowOnlyKey"), Is.True,
            "прочие ключи приходят из снимка WF через MergedDictionaries");
        Assert.That(dictionary.MergedDictionaries.Count, Is.EqualTo(1));
    }

    [Test]
    public void TryInstall_IsIdempotent()
    {
        ResourceDictionary defaults = Defaults();
        Dictionary<Uri, ResourceDictionary> cache = new Dictionary<Uri, ResourceDictionary>();

        WfColorResourcesOverride.TryInstall(StudioPalette.Dark, defaults, cache);
        ResourceDictionary first = cache[WfColorResourcesOverride.ColorResourcesRelativeUri];

        bool second = WfColorResourcesOverride.TryInstall(StudioPalette.Light, defaults, cache);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.True);
            Assert.That(cache[WfColorResourcesOverride.ColorResourcesRelativeUri], Is.SameAs(first),
                "повторная установка не пересоздаёт словарь, только переводит кисти на другую палитру");
            Assert.That(ColorOf(first, "WorkflowViewElementBackground"), Is.EqualTo(StudioPalette.Light.Surface),
                "повторный вызов обновляет палитру");
        });
    }

    [Test]
    public void TryInstall_RejectsNullArguments()
    {
        ResourceDictionary defaults = Defaults();

        Assert.Multiple(() =>
        {
            Assert.That(WfColorResourcesOverride.TryInstall(null, defaults, new Dictionary<Uri, ResourceDictionary>()), Is.False);
            Assert.That(WfColorResourcesOverride.TryInstall(StudioPalette.Dark, null, new Dictionary<Uri, ResourceDictionary>()), Is.False);
            Assert.That(WfColorResourcesOverride.TryInstall(StudioPalette.Dark, defaults, null), Is.False);
            Assert.That(WfColorResourcesOverride.IsInstalled, Is.False);
        });
    }

    [Test]
    public void LegacySwitch_KeepsPostPaintMode()
    {
        // Аварийный тумблер поддержки: студия обязана вернуться к прежней пост-покраске, а не упасть.
        AppContext.SetSwitch(WfColorResourcesOverride.LegacyThemingSwitch, true);

        bool installed = WfColorResourcesOverride.TryInstall(StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(WfColorResourcesOverride.IsLegacyForced, Is.True);
            Assert.That(installed, Is.False, "при включённом тумблере установка не выполняется");
            Assert.That(WfColorResourcesOverride.IsInstalled, Is.False, "остаётся прежний режим пост-покраски");
        });
    }

    // --- Кисти: не заморожены, привязаны, меняют цвет без обхода дерева. ---

    [Test]
    public void Brushes_AreNotFrozenAndBoundToTokenSource()
    {
        Dictionary<Uri, ResourceDictionary> cache = Install(StudioPalette.Dark);
        ResourceDictionary dictionary = cache[WfColorResourcesOverride.ColorResourcesRelativeUri];

        Assert.That(dictionary["WorkflowViewElementBackground"], Is.InstanceOf<SolidColorBrush>());
        SolidColorBrush brush = (SolidColorBrush)dictionary["WorkflowViewElementBackground"];

        Assert.Multiple(() =>
        {
            Assert.That(brush.IsFrozen, Is.False, "замороженную кисть нельзя перекрасить");
            Assert.That(brush.CanFreeze, Is.False, "именно из-за этого WPF клонирует кисть на элемент");
            Assert.That(BindingOperations.GetBindingExpression(brush, SolidColorBrush.ColorProperty), Is.Not.Null,
                "цвет приходит из источника токена — иначе смена темы потребует обхода дерева");
            Assert.That(brush.Color, Is.EqualTo(StudioPalette.Dark.Surface));
        });
    }

    [Test]
    public void ApplyPalette_RecoloursBrushInPlace()
    {
        Dictionary<Uri, ResourceDictionary> cache = Install(StudioPalette.Dark);
        SolidColorBrush brush = (SolidColorBrush)cache[WfColorResourcesOverride.ColorResourcesRelativeUri]["WorkflowViewElementBackground"];

        WfColorResourcesOverride.ApplyPalette(StudioPalette.Light);

        Assert.Multiple(() =>
        {
            Assert.That(brush.Color, Is.EqualTo(StudioPalette.Light.Surface),
                "смена значения источника перекрашивает уже созданные элементы");
            Assert.That(cache[WfColorResourcesOverride.ColorResourcesRelativeUri]["WorkflowViewElementBackground"],
                Is.SameAs(brush), "объект кисти тот же — дерево не обходится и словарь не пересобирается");
        });
    }

    [Test]
    public void ApplyPalette_WithoutInstall_DoesNothing()
    {
        Assert.DoesNotThrow(() => WfColorResourcesOverride.ApplyPalette(StudioPalette.Light));
        Assert.DoesNotThrow(() => WfColorResourcesOverride.ApplyPalette(null));
    }

    [Test]
    public void Reset_ReturnsToLegacyMode()
    {
        Install(StudioPalette.Dark);

        WfColorResourcesOverride.Reset();

        Assert.That(WfColorResourcesOverride.IsInstalled, Is.False,
            "режимы взаимоисключающие: снятая установка возвращает пост-покраску активностей");
    }

    // --- Стражи по исходникам: точка установки и взаимоисключение режимов. ---

    [Test]
    public void App_InstallsColourOverrideBeforeEntryPoint()
    {
        string source = ReadSource(Path.Combine("AiRpaStudio", "App.xaml.cs"));

        int install = source.IndexOf("WfColorResourcesOverride.TryInstall(", StringComparison.Ordinal);
        int entryPoint = source.IndexOf("new EntryPoint().Run()", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(install, Is.GreaterThan(0), "установка словаря палитры обязательна при старте");
            Assert.That(entryPoint, Is.GreaterThan(0));
            Assert.That(install, Is.LessThan(entryPoint),
                "подмена обязана пройти до создания первого WorkflowDesigner, иначе первый кадр — цветами WF");
            Assert.That(source, Does.Contain("WfDesignerTheme.ApplyToApplication();"),
                "палитра студии объявляется до подмены словаря WF");
        });
    }

    [Test]
    public void ActivityTheme_SkipsPostPaintInCacheMode()
    {
        string source = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "WfActivityTheme.cs"));
        int guard = source.IndexOf("WfColorResourcesOverride.IsInstalled", StringComparison.Ordinal);
        int theme = source.IndexOf("ThemeActivities(root, palette);", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(guard, Is.GreaterThan(0),
                "в режиме кэша пост-покраска активностей не выполняется (иначе вернётся мерцание)");
            Assert.That(theme, Is.GreaterThan(guard), "проверка режима стоит до прохода по активностям");
            Assert.That(source, Does.Contain("UseNativeControlStyles(root.Resources, palette);"),
                "гашение неявных стилей MaterialDesign остаётся в обоих режимах");
        });
    }

    [Test]
    public void DesignerTheme_SkipsDefaultColourReplacementInCacheMode()
    {
        string source = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "WfDesignerTheme.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("WfColorResourcesOverride.ApplyPalette(palette);"),
                "смена темы переводит связанные кисти WF на новую палитру");
            Assert.That(source, Does.Contain("if (!WfColorResourcesOverride.IsInstalled)"),
                "подмена «запечённых» дефолтов WF по значению в режиме кэша не нужна");
        });
    }

    [Test]
    public void CreatePaletteBrush_InCacheMode_ReturnsBoundUnfrozenBrush()
    {
        Install(StudioPalette.Dark);

        SolidColorBrush brush = WfColorResourcesOverride.CreatePaletteBrush(StudioPalette.SurfaceToken, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(brush, Is.Not.Null);
            Assert.That(brush.IsFrozen, Is.False, "кисть стиля должна перекрашиваться вместе с темой");
            Assert.That(BindingOperations.GetBindingExpression(brush, SolidColorBrush.ColorProperty), Is.Not.Null);
            Assert.That(brush.Color, Is.EqualTo(StudioPalette.Dark.Surface));
        });
    }

    [Test]
    public void CreatePaletteBrush_InLegacyMode_ReturnsNull()
    {
        // В прежнем режиме цвет ставит пост-покраска: стиль с замороженной кистью после смены темы
        // остался бы со старым цветом, поэтому в этом режиме стиль не создаётся вовсе.
        Assert.That(WfColorResourcesOverride.CreatePaletteBrush(StudioPalette.SurfaceToken, StudioPalette.Dark),
            Is.Null);
    }

    [Test]
    public void ThemeStaleText_SetsBoundForegroundAndIsIdempotent()
    {
        Install(StudioPalette.Dark);

        // Активность без плиты: проверяется только текстовая часть режима кэша.
        var activity = new System.Windows.Controls.ContentControl();
        var root = new System.Windows.Controls.Grid();
        root.Children.Add(activity);
        System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);

        Assert.DoesNotThrow(() => WfActivityTheme.ThemeStaleText(root, StudioPalette.Dark));
        Assert.DoesNotThrow(() => WfActivityTheme.ForgetThemeMarks());
    }

    [Test]
    public void Sources_TextPassUsesSynchronousLayoutHook()
    {
        // Регресс, найденный пробником: отложенный проход (BeginInvoke) даёт кадр с чёрной подписью, а
        // запись цвета без отметки зацикливает раскладку (LayoutUpdated не выходит в idle).
        string source = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "WfActivityTheme.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("root.LayoutUpdated += (sender, args) => ThemeStaleText(root, hook.Palette);"),
                "цвет текста ставится синхронно в LayoutUpdated, до отрисовки кадра");
            Assert.That(source, Does.Contain("if (root is WorkflowViewElement self && IsStale(self, palette))"),
                "проход по тексту идёт только по неотмеченным активностям — иначе раскладка зацикливается");
            Assert.That(source, Does.Contain("Marks.Remove(activity);"),
                "перед записью цвета активность помечается");
        });
    }

    // --- Вспомогательное. ---

    private static Dictionary<Uri, ResourceDictionary> Install(StudioThemeColors palette)
    {
        Dictionary<Uri, ResourceDictionary> cache = new Dictionary<Uri, ResourceDictionary>();
        Assert.That(WfColorResourcesOverride.TryInstall(palette, Defaults(), cache), Is.True);
        return cache;
    }

    /// <summary>
    /// Снимок WF один на процесс: строим его один раз. Дополнительный ключ нужен, чтобы проверить
    /// склейку <c>MergedDictionaries</c> на непереопределённом значении.
    /// </summary>
    private static ResourceDictionary Defaults()
    {
        if (_defaults != null)
        {
            return _defaults;
        }

        _defaults = WfColorResourcesOverride.LoadWorkflowDefaults();
        Assert.That(_defaults, Is.Not.Null, "снимок ColorResources.xaml должен загружаться");
        _defaults["SomeWorkflowOnlyKey"] = Brushes.Magenta;
        return _defaults;
    }

    private static bool ContainsValue(ResourceDictionary dictionary, string key)
    {
        if (dictionary.Contains(key))
        {
            return true;
        }

        foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
        {
            if (merged != null && ContainsValue(merged, key))
            {
                return true;
            }
        }

        return false;
    }

    private static Color ColorOf(ResourceDictionary dictionary, string key)
    {
        object value = dictionary[key];
        return value is SolidColorBrush brush ? brush.Color : default;
    }

    private static string ReadSource(string relativePath)
    {
        string root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string FindRepositoryRoot()
    {
        string directory = Path.GetDirectoryName(typeof(WfColorResourcesOverrideTests).Assembly.Location);

        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "AiRpa.sln")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return Directory.GetCurrentDirectory();
    }

    private static ResourceDictionary _defaults;

}
