using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи оптимизаций запуска AiRpaStudio (план
/// <c>StartupPerformance_Test_Coverage_Plan.md</c>): то, что нельзя проверить прогоном,
/// проверяется чтением исходников. Каждый тест фиксирует конкретный оптимизированный путь в точке
/// вызова — если кто-то вернёт синхронную пересборку палитры, запрос обновлений при старте или
/// снимет фоновый <c>Task.Run</c>, соответствующий страж обязан упасть.
/// </summary>
[TestFixture]
public class StartupPerformanceGuardTests
{
    private static string ReadSource(params string[] parts)
    {
        string root = FindRepositoryRoot();
        string path = IOPath.Combine(new[] { root }.Concat(parts).ToArray());
        Assert.That(File.Exists(path), Is.True, "не найден исходник: " + path);
        return File.ReadAllText(path);
    }

    [Test]
    public void MainView_DefersNuGetInitToApplicationIdle()
    {
        string mainView = ReadSource("AiRpaStudio", "Views", "MainView.xaml.cs");

        Assert.That(mainView, Does.Contain("DispatcherPriority.ApplicationIdle"),
            "инициализация NuGet-панели и сканирование пакетов обязаны откладываться до ApplicationIdle");
    }

    [Test]
    public void MainView_ScansPackageActivitiesInBackground()
    {
        string mainView = ReadSource("AiRpaStudio", "Views", "MainView.xaml.cs");

        Assert.That(mainView, Does.Contain("Task.Run(() => new Core.Workflow.NuGetActivityPaletteLoader().LoadFromProject(projectPath))"),
            "сканирование пакетных активностей обязано выполняться в фоне (Task.Run)");
    }

    [Test]
    public void MainView_RebuildsPaletteInBackground()
    {
        string mainView = ReadSource("AiRpaStudio", "Views", "MainView.xaml.cs");

        Assert.That(mainView, Does.Contain("Task.Run(BuildPaletteModel)"),
            "пересборка модели палитры обязана выполняться в фоне, а не на UI-потоке");
    }

    [Test]
    public void MainView_HasNoSynchronousToolboxBuild()
    {
        string mainView = ReadSource("AiRpaStudio", "Views", "MainView.xaml.cs");

        Assert.That(mainView, Does.Not.Contain("new WFToolboxHelper().Build()"),
            "в MainView не должно остаться синхронного WFToolboxHelper.Build() — только BuildModel (фон) + BuildControl (UI)");
    }

    [Test]
    public void Main_LoadsProjectAsync()
    {
        string main = ReadSource("AiRpaStudio", "Models", "Main.cs");

        Assert.Multiple(() =>
        {
            Assert.That(main, Does.Contain("_ = LoadProjectIntoStudioAsync(lastProjectPath)"),
                "Main.Loaded обязан стартовать загрузку проекта асинхронно");
            Assert.That(main, Does.Contain("Task.Run(() => ProjectManager.BuildTree(path))"),
                "дерево проекта (native и VS) строится в фоне единым путём");
            Assert.That(main, Does.Contain("OnUiThreadAsync"),
                "создание/привязка дизайнеров возвращается на UI-поток через OnUiThreadAsync");
        });
    }

    [Test]
    public void BuildPaletteModel_PersistsDiskCache()
    {
        string mainView = ReadSource("AiRpaStudio", "Views", "MainView.xaml.cs");

        Assert.That(mainView, Does.Contain("registry.PersistDiskCache()"),
            "фоновая сборка палитры обязана прогревать дисковый кэш для холодного старта");
    }

    [Test]
    public void NuGetPanel_ShownHookIsWired()
    {
        string nuGetPanel = ReadSource("RPA.NuGet", "Views", "NuGetPanel.xaml.cs");

        Assert.That(nuGetPanel, Does.Contain("_viewModel.NotifyPanelShown()"),
            "панель обязана сообщать модели о показе (отложенный первичный поиск)");
    }

    [Test]
    public void ActivityPaletteRegistry_UsesReflectionCaches()
    {
        string registry = ReadSource("AiRpaStudio", "Core", "Generation", "ActivityPaletteRegistry.cs");

        Assert.Multiple(() =>
        {
            Assert.That(registry, Does.Contain("DescriptorCache.GetOrAdd"),
                "дескрипторы обязаны мемоизироваться в DescriptorCache");
            Assert.That(registry, Does.Contain("LocalizedStringCache.GetOrAdd"),
                "локализованные строки обязаны кэшироваться");
            Assert.That(registry, Does.Contain("ResourceManagerCache.GetOrAdd"),
                "ResourceManager обязан кэшироваться");
            Assert.That(registry, Does.Contain("AttributeDataCache.GetValue"),
                "сырые CustomAttributeData обязаны кэшироваться по члену типа");
            Assert.That(registry, Does.Not.Contain("GetCustomAttribute<"),
                "в горячем пути не должно быть прямого GetCustomAttribute<T> (резолвит дизайнерские атрибуты и падает)");
        });
    }

    [Test]
    public void MainView_PrepareLayout_UsesStagingDockingManager()
    {
        string mainView = ReadSource("AiRpaStudio", "Views", "MainView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(mainView, Does.Contain("DockingManager staging = new DockingManager()"),
                "десериализация раскладки обязана идти в невидимый staging-DockingManager");
            Assert.That(mainView, Does.Contain("new XmlLayoutSerializer(staging)"),
                "сериализатор обязан читать в staging, а не в реальный dockManager");
        });
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory != null && !File.Exists(IOPath.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");

        return directory.FullName;
    }
}
