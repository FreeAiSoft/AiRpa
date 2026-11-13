using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Agent.Core.Services;

using NUnit.Framework;
using RPA.Helpers;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// `CP-08` — зависимости робота разрешаются из его собственного выходного каталога.
/// <para>
/// Робот на 4.7.2 приходит со своими пакетами (`bin\Debug` или `bin\Debug\net48`). Студия
/// на 4.8 обязана находить их при десериализации XAML, но не имеет права подменять ими
/// студийные `System.*`, WF, `Activities*`, `ActivitiesTracker`, `RPA*` — иначе один AppDomain
/// получает две версии одной сборки на два рантайма.
/// </para>
/// </summary>
[TestFixture]
public class RobotAssemblyResolverTests
{
    private string probeDirectory;

    [SetUp]
    public void SetUp()
    {
        probeDirectory = IOPath.Combine(IOPath.GetTempPath(), "robot_probe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(probeDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        RobotAssemblyResolver.Uninstall();

        try
        {
            if (Directory.Exists(probeDirectory))
            {
                Directory.Delete(probeDirectory, true);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Загруженная в домен сборка держит файл — каталог останется во временной папке.
        }
        catch (IOException)
        {
            // Загруженная в домен сборка держит файл — каталог останется во временной папке.
        }
    }

    // --- Поиск файла сборки в каталоге робота. ---

    [Test]
    public void FindAssemblyPath_DependencyOfRobot_IsFoundInProbeDirectory()
    {
        string expected = CopyToProbe(typeof(Newtonsoft.Json.JsonConvert).Assembly);

        string found = RobotAssemblyResolver.FindAssemblyPath(
            typeof(Newtonsoft.Json.JsonConvert).Assembly.FullName, new[] { probeDirectory });

        Assert.That(found, Is.EqualTo(expected), "собственный пакет робота обязан находиться в его каталоге");
    }

    [Test]
    public void FindAssemblyPath_RequestBySimpleName_IsResolved()
    {
        // WF грузит часть сборок по простому имени, без версии и ключа.
        CopyToProbe(typeof(Newtonsoft.Json.JsonConvert).Assembly);

        string found = RobotAssemblyResolver.FindAssemblyPath("Newtonsoft.Json", new[] { probeDirectory });

        Assert.That(found, Is.Not.Null);
        Assert.That(IOPath.GetFileName(found), Is.EqualTo("Newtonsoft.Json.dll"));
    }

    [Test]
    public void FindAssemblyPath_NoDowngrade()
    {
        CopyToProbe(typeof(Newtonsoft.Json.JsonConvert).Assembly);

        string found = RobotAssemblyResolver.FindAssemblyPath(
            "Newtonsoft.Json, Version=99.0.0.0, Culture=neutral, PublicKeyToken=30ad4fe6b2a6aeed",
            new[] { probeDirectory });

        Assert.That(found, Is.Null, "понижение версии запрещено: робот не подменяет запрошенную сборку старее");
    }

    [Test]
    public void FindAssemblyPath_PublicKeyTokenMismatch_ReturnsNull()
    {
        CopyToProbe(typeof(Newtonsoft.Json.JsonConvert).Assembly);

        string found = RobotAssemblyResolver.FindAssemblyPath(
            "Newtonsoft.Json, Version=13.0.0.0, Culture=neutral, PublicKeyToken=0000000000000000",
            new[] { probeDirectory });

        Assert.That(found, Is.Null, "другой открытый ключ — это другая сборка");
    }

    [Test]
    public void FindAssemblyPath_HostOwnedAssembly_IsNeverServedFromRobotFolder()
    {
        // Прямая проверка приоритета: даже если копия студийной сборки лежит в каталоге робота,
        // она не подставляется — хост использует свою.
        Assembly tracker = typeof(ActivitiesTracker.Core.CustomTrackingParticipant).Assembly;
        CopyToProbe(tracker);

        string found = RobotAssemblyResolver.FindAssemblyPath(tracker.FullName, new[] { probeDirectory });

        Assert.That(found, Is.Null, "сборка `ActivitiesTracker` принадлежит хосту и не обслуживается из каталога робота");
    }

    [Test]
    public void FindAssemblyPath_MissingFileOrDirectory_ReturnsNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RobotAssemblyResolver.FindAssemblyPath("Newtonsoft.Json, Version=13.0.0.0", new[] { probeDirectory }), Is.Null);
            Assert.That(RobotAssemblyResolver.FindAssemblyPath("Newtonsoft.Json", new[] { IOPath.Combine(probeDirectory, "nope") }), Is.Null);
            Assert.That(RobotAssemblyResolver.FindAssemblyPath(null, new[] { probeDirectory }), Is.Null);
            Assert.That(RobotAssemblyResolver.FindAssemblyPath("   ", new[] { probeDirectory }), Is.Null);
            Assert.That(RobotAssemblyResolver.FindAssemblyPath("!!! not an assembly name !!!", new[] { probeDirectory }), Is.Null);
            Assert.That(RobotAssemblyResolver.FindAssemblyPath("Newtonsoft.Json", null), Is.Null);
        });
    }

    [TestCase("System.Activities", true)]
    [TestCase("System.Xaml", true)]
    [TestCase("mscorlib", true)]
    [TestCase("PresentationFramework", true)]
    [TestCase("Activities", true)]
    [TestCase("Activities.Browser", true)]
    [TestCase("ActivitiesTracker", true)]
    [TestCase("ActivityLibraryBase", true)]
    [TestCase("RPA", true)]
    [TestCase("RpaCommon", true)]
    [TestCase("FW.EasyMvvm", true)]
    [TestCase("AvalonDock", true)]
    [TestCase("Newtonsoft.Json", false)]
    [TestCase("MyRobot.Helpers", false)]
    [TestCase("SystemX", false)]
    public void IsHostOwnedAssemblyName_Table(string name, bool expected)
    {
        Assert.That(RobotAssemblyResolver.IsHostOwnedAssemblyName(name), Is.EqualTo(expected));
    }

    // --- Каталоги поиска: bin\<Config> и bin\<Config>\<TFM>. ---

    [Test]
    public void GetProbeDirectories_CoversBothProjectLayouts()
    {
        string project = IOPath.Combine(probeDirectory, "Robot");
        string debug = IOPath.Combine(project, "bin", "Debug");
        string debugNet48 = IOPath.Combine(debug, "net48");
        Directory.CreateDirectory(debugNet48);

        List<string> directories = RobotAssemblyResolver.GetProbeDirectories(project);

        Assert.That(directories, Does.Contain(debug), "legacy-проект кладёт сборку прямо в bin\\Debug");
        Assert.That(directories, Does.Contain(debugNet48), "SDK-style кладёт сборку в bin\\Debug\\net48");
        Assert.That(directories.First(), Is.EqualTo(debug), "каталог bin\\Debug идёт первым");
    }

    [Test]
    public void GetProbeDirectories_AbsentProject_IsEmpty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RobotAssemblyResolver.GetProbeDirectories(null), Is.Empty);
            Assert.That(RobotAssemblyResolver.GetProbeDirectories("   "), Is.Empty);
            Assert.That(RobotAssemblyResolver.GetProbeDirectories(IOPath.Combine(probeDirectory, "Absent")), Is.Empty);
            Assert.That(RobotAssemblyResolver.GetProbeDirectories(probeDirectory), Is.Empty, "у каталога без bin\\<Config> каталогов поиска нет");
        });
    }

    [Test]
    public void GetProbeDirectories_CoversPlatformLayouts()
    {
        string project = IOPath.Combine(probeDirectory, "Robot");
        string debug = IOPath.Combine(project, "bin", "Debug");
        string x64Debug = IOPath.Combine(project, "bin", "x64", "Debug");
        string x64DebugNet48 = IOPath.Combine(x64Debug, "net48");
        string x86Release = IOPath.Combine(project, "bin", "x86", "Release");
        Directory.CreateDirectory(debug);
        Directory.CreateDirectory(x64DebugNet48);
        Directory.CreateDirectory(x86Release);

        List<string> directories = RobotAssemblyResolver.GetProbeDirectories(project);

        Assert.Multiple(() =>
        {
            Assert.That(directories, Does.Contain(debug), "AnyCPU: bin/Debug");
            Assert.That(directories, Does.Contain(x64Debug), "x64: bin/x64/Debug");
            Assert.That(directories, Does.Contain(x64DebugNet48), "x64 + TFM: bin/x64/Debug/net48");
            Assert.That(directories, Does.Contain(x86Release), "x86 Release: bin/x86/Release");
            Assert.That(directories.First(), Is.EqualTo(debug), "первым идёт bin/Debug — как в RobotLocator");
            Assert.That(directories, Is.Unique);
        });
    }

    // --- Подписка на AppDomain.AssemblyResolve. ---

    [Test]
    public void Install_SubscribesOnlyOnce()
    {
        RobotAssemblyResolver.Install(AppDomain.CurrentDomain, new[] { probeDirectory });
        int afterFirst = RobotAssemblyResolver.SubscribeCount;

        RobotAssemblyResolver.Install(AppDomain.CurrentDomain, new[] { probeDirectory });

        Assert.Multiple(() =>
        {
            Assert.That(afterFirst, Is.EqualTo(1));
            Assert.That(RobotAssemblyResolver.SubscribeCount, Is.EqualTo(1), "повторный Install не вешает второй обработчик");
            Assert.That(RobotAssemblyResolver.IsInstalled, Is.True);
            Assert.That(RobotAssemblyResolver.ProbeDirectories, Is.EqualTo(new[] { probeDirectory }));
        });
    }

    [Test]
    public void Install_WithNewDirectories_ReplacesThem()
    {
        string second = IOPath.Combine(probeDirectory, "Second");
        Directory.CreateDirectory(second);

        RobotAssemblyResolver.Install(AppDomain.CurrentDomain, new[] { probeDirectory });
        RobotAssemblyResolver.Install(AppDomain.CurrentDomain, new[] { second });

        Assert.Multiple(() =>
        {
            Assert.That(RobotAssemblyResolver.SubscribeCount, Is.EqualTo(1));
            Assert.That(RobotAssemblyResolver.ProbeDirectories, Is.EqualTo(new[] { second }));
        });
    }

    [Test]
    public void Install_WithoutDirectoriesOrDomain_DoesNotSubscribe()
    {
        RobotAssemblyResolver.Install(AppDomain.CurrentDomain, null);
        RobotAssemblyResolver.Install(AppDomain.CurrentDomain, Array.Empty<string>());
        RobotAssemblyResolver.Install(null, new[] { probeDirectory });

        Assert.That(RobotAssemblyResolver.SubscribeCount, Is.Zero);
        Assert.That(RobotAssemblyResolver.IsInstalled, Is.False);
    }

    [Test]
    public void InstallForProject_WithoutRealProject_IsSilent()
    {
        Assert.DoesNotThrow(() => RobotAssemblyResolver.InstallForProject(null));
        Assert.DoesNotThrow(() => RobotAssemblyResolver.InstallForProject(IOPath.Combine(probeDirectory, "Absent")));

        Assert.That(RobotAssemblyResolver.IsInstalled, Is.False, "нечего искать — подписки нет");
    }

    [Test]
    public void Uninstall_RemovesSubscriptionAndDirectories()
    {
        RobotAssemblyResolver.Install(AppDomain.CurrentDomain, new[] { probeDirectory });
        RobotAssemblyResolver.Uninstall();

        Assert.Multiple(() =>
        {
            Assert.That(RobotAssemblyResolver.SubscribeCount, Is.Zero);
            Assert.That(RobotAssemblyResolver.IsInstalled, Is.False);
            Assert.That(RobotAssemblyResolver.ProbeDirectories, Is.Empty);
        });
    }

    // --- Обработчик. ---

    [Test]
    public void ResolveAssembly_ServesDependencyOfRobot()
    {
        CopyToProbe(typeof(Newtonsoft.Json.JsonConvert).Assembly);
        RobotAssemblyResolver.Install(AppDomain.CurrentDomain, new[] { probeDirectory });

        Assembly resolved = RobotAssemblyResolver.ResolveAssembly(
            this, new ResolveEventArgs("Newtonsoft.Json, Version=13.0.0.0, Culture=neutral, PublicKeyToken=30ad4fe6b2a6aeed"));

        Assert.That(resolved, Is.Not.Null, "зависимость робота обязана разрешиться");
        Assert.That(resolved.GetName().Name, Is.EqualTo("Newtonsoft.Json"));
    }

    [Test]
    public void ResolveAssembly_ReturnsAlreadyLoadedAssembly_WithoutLockingProbe()
    {
        // Репродукция инцидента MSB3021/MSB3027: entry-сборка робота уже загружена движком из
        // байтов, а в bin лежит её же копия. Обработчик обязан вернуть уже загруженную сборку,
        // а не грузить копию из bin (иначе копия блокируется и автосборка падает).
        Assembly loaded = typeof(RobotAssemblyResolverTests).Assembly;
        string copied = CopyToProbe(loaded);

        RobotAssemblyResolver.Install(AppDomain.CurrentDomain, new[] { probeDirectory });

        Assembly resolved = RobotAssemblyResolver.ResolveAssembly(
            this, new ResolveEventArgs(loaded.FullName));

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.SameAs(loaded),
                "запрос уже загруженной сборки обязан вернуть её же, а не второй экземпляр");
            Assert.That(FileLockDetector.IsFileLocked(copied), Is.False,
                "копия в bin не должна блокироваться обработчиком зависимостей");
        });
    }

    [Test]
    public void ResolveAssembly_UnresolvableRequest_ReturnsNullWithoutThrowing()
    {
        RobotAssemblyResolver.Install(AppDomain.CurrentDomain, new[] { probeDirectory });

        Assert.Multiple(() =>
        {
            Assert.That(RobotAssemblyResolver.ResolveAssembly(this, null), Is.Null);
            Assert.That(RobotAssemblyResolver.ResolveAssembly(this, new ResolveEventArgs(string.Empty)), Is.Null);
            Assert.That(RobotAssemblyResolver.ResolveAssembly(this, new ResolveEventArgs("Nope.Missing, Version=1.0.0.0")), Is.Null);
            Assert.That(RobotAssemblyResolver.ResolveAssembly(this, new ResolveEventArgs("System.Core, Version=4.0.0.0")), Is.Null);
        });
    }

    // --- Проводка в студии. ---

    [Test]
    public void ActivityRunner_InstallsResolverBeforeLoadingXaml()
    {
        string[] lines = File.ReadAllLines(IOPath.Combine(FindRepositoryRoot(), "AiRpaStudio", "Core", "ActivityRunner.cs"));

        // Ищем именно вызов, а не упоминание в комментарии: строка обязана начинаться с вызова.
        int install = Array.FindIndex(lines, line => line.TrimStart().StartsWith("RobotAssemblyResolver.InstallForProject(", StringComparison.Ordinal));
        int load = Array.FindIndex(lines, line => line.Contains("LoadActivityToDesignerHelper.LoadActivityToDesigner("));

        Assert.That(install, Is.GreaterThan(-1), "запуск робота обязан ставить обработчик поиска зависимостей");
        Assert.That(load, Is.GreaterThan(-1), "в ActivityRunner ожидается загрузка активности в дизайнер");
        Assert.That(install, Is.LessThan(load), "обработчик ставится до десериализации XAML, иначе зависимости робота уже не найти");
    }

    // --- Инфраструктура. ---

    /// <summary>Копирует сборку в каталог-«робот» и возвращает полный путь копии.</summary>
    private string CopyToProbe(Assembly assembly)
    {
        string fileName = IOPath.GetFileName(assembly.Location);
        string target = IOPath.Combine(probeDirectory, fileName);

        File.Copy(assembly.Location, target, true);

        return target;
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
