using System;
using System.Activities;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RPA.Chat.Services.Workflow;
using RPA.Core.Generation;
using RPA.Core.Workflow;

namespace AiRpaStudio.Tests.Workflow;

/// <summary>
/// AG-08, видимость результата (вариант B): после успешной сборки тип новой активности обязан появиться
/// в каталоге агента и в палитре <b>без перезапуска студии</b>.
/// <para>
/// Проверяется без WPF и без MSBuild: загрузчик сборки подменяется, а «сборкой» служит сама тестовая
/// сборка (в ней объявлена <see cref="PaletteRefreshProbeActivity"/>). Пересборка панели «Активности»
/// представлена колбэком — тест считает вызовы; сама подмена содержимого панели остаётся в `MainView`.
/// </para>
/// <para>
/// Негативы здесь же: без загрузки в домен тип не появляется, сборка без активностей и файл с чужим
/// именем дают «не выполнено», а не молчаливый успех.
/// </para>
[TestFixture]
public class ActivityPaletteRefreshTests
{
    [TearDown]
    public void TearDown()
    {
        StudioActivityPalette.ResetUserLibraries();
        StudioActivityCatalog.InvalidateDefault();
        ActivityPaletteRegistry.InvalidateDefault();
    }

    [Test]
    public void Notify_LoadsAssembly_AndTypeAppearsInCatalogWithoutRestart()
    {
        // Кэши прогреваются ДО создания активности: иначе Lazy инициализировался бы уже после
        // регистрации типов и «сброс кэшей» не проверялся бы вовсе (негатив B это и поймал).
        StudioActivityCatalog catalogBefore = StudioActivityCatalog.Default;
        ActivityPaletteRegistry registryBefore = ActivityPaletteRegistry.Default;
        Assert.That(catalogBefore.Describe(nameof(PaletteRefreshProbeActivity)), Is.Null,
            "до создания активности типа в каталоге быть не должно");


        int panelRefreshes = 0;
        var notifier = new StudioActivityPaletteNotifier(path => typeof(PaletteRefreshProbeActivity).Assembly, () => panelRefreshes++);

        ActivityPaletteRefreshResult result = notifier.Notify(typeof(PaletteRefreshProbeActivity).Assembly.Location);

        Assert.Multiple(() =>
        {
            Assert.That(result.Refreshed, Is.True, result.Reason);
            Assert.That(result.ActivityTypeCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(result.AssemblyName, Is.EqualTo(typeof(PaletteRefreshProbeActivity).Assembly.GetName().Name));
        });

        ActivityInfo described = StudioActivityCatalog.Default.Describe(nameof(PaletteRefreshProbeActivity));
        Assert.Multiple(() =>
        {
            Assert.That(described, Is.Not.Null, "новая активность не попала в каталог агента");
            Assert.That(described.TypeName, Does.Contain(nameof(PaletteRefreshProbeActivity)));
            Assert.That(StudioActivityPalette.Categories().SelectMany(category => category.Types),
                Does.Contain(typeof(PaletteRefreshProbeActivity)), "новой активности нет в палитре");
            Assert.That(panelRefreshes, Is.EqualTo(1), "панель «Активности» не обновлена");
            Assert.That(StudioActivityCatalog.Default, Is.Not.SameAs(catalogBefore), "кэш каталога не сброшен");
            Assert.That(ActivityPaletteRegistry.Default, Is.Not.SameAs(registryBefore), "кэш реестра не сброшен");
        });
    }


    [Test]
    public void Notify_WithoutLoadingAssembly_TypeDoesNotAppear()
    {
        var notifier = new StudioActivityPaletteNotifier(
            path => throw new FileLoadException("сборка не загрузилась"),
            () => { });

        ActivityPaletteRefreshResult result = notifier.Notify(typeof(PaletteRefreshProbeActivity).Assembly.Location);

        Assert.Multiple(() =>
        {
            Assert.That(result.Refreshed, Is.False);
            Assert.That(result.Reason, Does.Contain("не загрузилась").Or.Contain("не удалось"));
            Assert.That(StudioActivityCatalog.Default.Describe(nameof(PaletteRefreshProbeActivity)), Is.Null,
                "без загрузки в домен тип не должен попадать в каталог");
        });
    }

    [Test]
    public void Notify_AssemblyWithoutActivities_IsNotPerformed()
    {
        int panelRefreshes = 0;
        var notifier = new StudioActivityPaletteNotifier(path => typeof(string).Assembly, () => panelRefreshes++);

        ActivityPaletteRefreshResult result = notifier.Notify(typeof(string).Assembly.Location);

        Assert.Multiple(() =>
        {
            Assert.That(result.Refreshed, Is.False);
            Assert.That(result.Reason, Does.Contain("активностей"));
            Assert.That(panelRefreshes, Is.EqualTo(0), "панель пересобиралась без причины");
        });
    }

    [Test]
    public void Notify_MissingFile_IsNotPerformed()
    {
        var notifier = new StudioActivityPaletteNotifier(path => typeof(PaletteRefreshProbeActivity).Assembly, () => { });

        ActivityPaletteRefreshResult result = notifier.Notify(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dll"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Refreshed, Is.False);
            Assert.That(result.Reason, Does.Contain("не найдена"));
        });
    }

    [Test]
    public void Notify_AssemblyNameMismatch_IsNotPerformed()
    {
        string path = Path.Combine(Path.GetTempPath(), "Other" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(path, new byte[] { 0x4D, 0x5A });

        try
        {
            var notifier = new StudioActivityPaletteNotifier(_ => typeof(PaletteRefreshProbeActivity).Assembly, () => { });

            ActivityPaletteRefreshResult result = notifier.Notify(path);

            Assert.Multiple(() =>
            {
                Assert.That(result.Refreshed, Is.False, "сборка с чужим именем не должна подменять ожидаемую");
                Assert.That(result.Reason, Does.Contain("не совпадает"));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Notify_Failure_DoesNotRefreshPanel()
    {
        int panelRefreshes = 0;
        var notifier = new StudioActivityPaletteNotifier(path => typeof(string).Assembly, () => panelRefreshes++);

        notifier.Notify(typeof(string).Assembly.Location);

        Assert.That(panelRefreshes, Is.EqualTo(0));
    }

    [Test]
    public void Reset_RemovesUserLibraryFromCatalog()
    {
        var notifier = new StudioActivityPaletteNotifier(path => typeof(PaletteRefreshProbeActivity).Assembly, () => { });
        notifier.Notify(typeof(PaletteRefreshProbeActivity).Assembly.Location);

        Assert.That(StudioActivityCatalog.Default.Describe(nameof(PaletteRefreshProbeActivity)), Is.Not.Null);

        StudioActivityPalette.ResetUserLibraries();
        StudioActivityCatalog.InvalidateDefault();
        ActivityPaletteRegistry.InvalidateDefault();

        Assert.That(StudioActivityCatalog.Default.Describe(nameof(PaletteRefreshProbeActivity)), Is.Null,
            "сброс пользовательских библиотек не очистил каталог");
    }

    [Test]
    public void InvalidateDefault_RebuildsBothCaches()
    {
        StudioActivityCatalog before = StudioActivityCatalog.Default;
        ActivityPaletteRegistry registryBefore = ActivityPaletteRegistry.Default;

        StudioActivityCatalog.InvalidateDefault();
        ActivityPaletteRegistry.InvalidateDefault();

        Assert.Multiple(() =>
        {
            Assert.That(StudioActivityCatalog.Default, Is.Not.SameAs(before), "каталог отдал прежний кэш");
            Assert.That(ActivityPaletteRegistry.Default, Is.Not.SameAs(registryBefore), "реестр отдал прежний кэш");
        });
    }

    [Test]
    public void Notify_RegistersTypesOnce_WhenCalledTwice()
    {
        var notifier = new StudioActivityPaletteNotifier(path => typeof(PaletteRefreshProbeActivity).Assembly, () => { });

        notifier.Notify(typeof(PaletteRefreshProbeActivity).Assembly.Location);
        notifier.Notify(typeof(PaletteRefreshProbeActivity).Assembly.Location);

        int occurrences = StudioActivityPalette.Categories()
            .SelectMany(category => category.Types)
            .Count(type => type == typeof(PaletteRefreshProbeActivity));

        Assert.That(occurrences, Is.EqualTo(1), "тип зарегистрирован дважды — палитра покажет дубль");
    }
}

/// <summary>Активность-проба: единственное, что нужно тестам видимости, — настоящий наследник WF-активности.</summary>
public sealed class PaletteRefreshProbeActivity : CodeActivity
{
    protected override void Execute(CodeActivityContext context)
    {
    }
}
