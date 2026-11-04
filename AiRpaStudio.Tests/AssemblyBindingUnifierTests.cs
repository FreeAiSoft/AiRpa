using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Баг «блокирующая ошибка FileLoadException в дизайнере при загрузке проекта».
/// <para>
/// WF-панель «Пространства имён» (<c>ImportDesigner.UpdateAvailableNamespaces</c>) перечисляет типы
/// сборок через <c>Assembly.GetExportedTypes()</c>. Если в папке приложения лежит
/// <c>System.Threading.Tasks.Extensions.dll</c> версии 4.2.0.0 (пакет 4.5.2), а конфигурация студии
/// перенаправляет запросы на 4.2.0.1 (пакет 4.5.4), загрузка зависимостей падает с
/// <c>FileLoadException</c> / HRESULT 0x80131040 («Найденное определение манифеста сборки не
/// соответствует ссылке на сборку»), и дизайнер показывает модальное окно, блокирующее студию.
/// </para>
/// <para>
/// Лечение двухуровневое: (1) единая версия пакета во всех проектах решения, (2) страховка в студии —
/// <see cref="AssemblyBindingUnifier"/> отдаёт уже загруженную сборку с тем же именем и ключом вместо
/// падения привязки. Здесь проверяются оба уровня.
/// </para>
/// </summary>
public class AssemblyBindingUnifierTests
{
    private const string SystemCoreToken = "b77a5c561934e089";

    // --- Поиск уже загруженной сборки. ---

    [Test]
    public void FindLoadedMatch_SameSimpleName_ReturnsLoadedAssembly()
    {
        Assembly systemCore = typeof(Enumerable).Assembly;
        string requested = $"System.Core, Version=1.2.3.4, Culture=neutral, PublicKeyToken={SystemCoreToken}";

        Assembly match = AssemblyBindingUnifier.FindLoadedMatch(requested, new[] { typeof(object).Assembly, systemCore });

        Assert.That(match, Is.SameAs(systemCore));
    }

    [Test]
    public void FindLoadedMatch_ExactVersionAndKey_ReturnsLoadedAssembly()
    {
        Assembly systemCore = typeof(Enumerable).Assembly;
        string requested = $"System.Core, Version={systemCore.GetName().Version}, Culture=neutral, PublicKeyToken={SystemCoreToken}";

        Assembly match = AssemblyBindingUnifier.FindLoadedMatch(requested, new[] { systemCore });

        Assert.That(match, Is.SameAs(systemCore));
    }

    [Test]
    public void FindLoadedMatch_UnknownName_ReturnsNull()
    {
        Assembly match = AssemblyBindingUnifier.FindLoadedMatch(
            "Definitely.Missing.Assembly, Version=1.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51",
            new[] { typeof(object).Assembly, typeof(Enumerable).Assembly });

        Assert.That(match, Is.Null);
    }

    [Test]
    public void FindLoadedMatch_EmptyOrBrokenName_ReturnsNullWithoutThrowing()
    {
        Assembly[] loaded = { typeof(object).Assembly };

        Assert.Multiple(() =>
        {
            Assert.That(AssemblyBindingUnifier.FindLoadedMatch(null, loaded), Is.Null);
            Assert.That(AssemblyBindingUnifier.FindLoadedMatch(string.Empty, loaded), Is.Null);
            Assert.That(AssemblyBindingUnifier.FindLoadedMatch("   ", loaded), Is.Null);
            Assert.That(AssemblyBindingUnifier.FindLoadedMatch("!!! not an assembly name !!!", loaded), Is.Null);
            Assert.That(AssemblyBindingUnifier.FindLoadedMatch("mscorlib, Version=4.0.0.0", null), Is.Null);
        });
    }

    [Test]
    public void FindLoadedMatch_PublicKeyTokenMismatch_ReturnsNull()
    {
        // Подмена сборки с другим открытым ключом недопустима: это уже другая сборка.
        Assembly match = AssemblyBindingUnifier.FindLoadedMatch(
            "System.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=0000000000000000",
            new[] { typeof(Enumerable).Assembly });

        Assert.That(match, Is.Null);
    }

    [Test]
    public void FindLoadedMatch_RequestWithoutVersion_ReturnsNull()
    {
        // WF грузит часть сборок по простому имени («System.Activities»). Такой запрос падает не из-за
        // версии, и подменять его сборкой «наугад» нельзя.
        Assembly match = AssemblyBindingUnifier.FindLoadedMatch(
            "System.Activities",
            new[] { typeof(object).Assembly, typeof(Enumerable).Assembly });

        Assert.That(match, Is.Null);
    }

    [Test]
    public void FindLoadedMatch_RequestWithoutPublicKeyToken_ReturnsNull()
    {
        // Ключа нет — сверить нечего, подмена может подсунуть чужую сборку.
        Assembly match = AssemblyBindingUnifier.FindLoadedMatch(
            "System.Core, Version=1.0.0.0, Culture=neutral",
            new[] { typeof(Enumerable).Assembly });

        Assert.That(match, Is.Null);
    }

    [Test]
    public void FindLoadedMatch_SatelliteResourceRequest_ReturnsNull()
    {
        // Сателлиты ресурсов (*.resources) не унифицируются: у каждой культуры своя сборка.
        Assembly match = AssemblyBindingUnifier.FindLoadedMatch(
            "AiRpaStudio.resources, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
            new[] { typeof(object).Assembly });

        Assert.That(match, Is.Null);
    }

    [Test]
    public void FindLoadedMatch_CultureSpecificRequest_ReturnsNull()
    {
        Assembly match = AssemblyBindingUnifier.FindLoadedMatch(
            $"System.Core, Version=1.0.0.0, Culture=ru, PublicKeyToken={SystemCoreToken}",
            new[] { typeof(Enumerable).Assembly });

        Assert.That(match, Is.Null);
    }

    [Test]
    public void FindLoadedMatch_DoesNotDowngrade()
    {
        // Понижение версии запрещено: загруженная 4.0.0.0 не заменяет запрошенную 99.0.0.0.
        Assembly match = AssemblyBindingUnifier.FindLoadedMatch(
            $"System.Core, Version=99.0.0.0, Culture=neutral, PublicKeyToken={SystemCoreToken}",
            new[] { typeof(Enumerable).Assembly });

        Assert.That(match, Is.Null);
    }

    // --- Обработчик события AppDomain.AssemblyResolve. ---

    [Test]
    public void Resolve_ReturnsLoadedAssemblyForMismatchedVersion()
    {
        Assembly systemCore = typeof(Enumerable).Assembly;
        string requested = $"System.Core, Version=1.2.3.4, Culture=neutral, PublicKeyToken={SystemCoreToken}";

        Assembly match = AssemblyBindingUnifier.ResolveAssembly(this, new ResolveEventArgs(requested));

        Assert.That(match, Is.SameAs(systemCore));
    }

    [Test]
    public void Resolve_UnresolvableRequest_ReturnsNullWithoutThrowing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AssemblyBindingUnifier.ResolveAssembly(this, new ResolveEventArgs("Nope.Missing, Version=1.0.0.0")), Is.Null);
            Assert.That(AssemblyBindingUnifier.ResolveAssembly(this, new ResolveEventArgs(string.Empty)), Is.Null);
            Assert.That(AssemblyBindingUnifier.ResolveAssembly(this, null), Is.Null);
        });
    }

    // --- Подписка в студии. ---

    [Test]
    public void Install_SubscribesOnlyOnce()
    {
        AssemblyBindingUnifier.Install(AppDomain.CurrentDomain);
        int afterFirst = AssemblyBindingUnifier.SubscribeCount;
        AssemblyBindingUnifier.Install(AppDomain.CurrentDomain);

        Assert.Multiple(() =>
        {
            Assert.That(afterFirst, Is.EqualTo(1), "первый Install подписывает обработчик");
            Assert.That(AssemblyBindingUnifier.SubscribeCount, Is.EqualTo(1), "повторный Install не должен вешать второй обработчик");
            Assert.That(AssemblyBindingUnifier.IsInstalled, Is.True);
        });
    }

    [Test]
    public void App_InstallsUnifierBeforeStartup()
    {
        string source = ReadSource(@"AiRpaStudio\App.xaml.cs");

        Assert.That(source, Does.Contain("AssemblyBindingUnifier.Install(AppDomain.CurrentDomain)"),
            "страховка от несогласованных версий обязана ставиться на старте приложения");
    }

    // --- Единая версия пакета в решении (корневая причина). ---

    [Test]
    public void Solution_UsesSingleVersionOfSystemThreadingTasksExtensions()
    {
        string root = FindRepositoryRoot();
        List<string> problems = new List<string>();

        foreach (string packagesConfig in EnumerateProjectFiles(root, "packages.config"))
        {
            XDocument document;
            try
            {
                document = XDocument.Load(packagesConfig);
            }
            catch (Exception ex)
            {
                problems.Add($"{Relative(root, packagesConfig)}: не читается ({ex.Message})");
                continue;
            }

            foreach (XElement package in document.Root.Elements("package"))
            {
                if (string.Equals((string)package.Attribute("id"), "System.Threading.Tasks.Extensions", StringComparison.OrdinalIgnoreCase)
                    && (string)package.Attribute("version") != "4.5.4")
                {
                    problems.Add($"{Relative(root, packagesConfig)}: версия {(string)package.Attribute("version")} вместо 4.5.4");
                }
            }
        }

        foreach (string project in EnumerateProjectFiles(root, "*.csproj"))
        {
            string text = File.ReadAllText(project);

            if (text.IndexOf("System.Threading.Tasks.Extensions.4.5.2", StringComparison.Ordinal) >= 0)
            {
                problems.Add($"{Relative(root, project)}: HintPath на пакет 4.5.2 (сборка 4.2.0.0)");
            }

            foreach (Match match in Regex.Matches(
                         text,
                         @"<PackageReference\s+Include=""System\.Threading\.Tasks\.Extensions""\s+Version=""([^""]+)"""))
            {
                if (match.Groups[1].Value != "4.5.4")
                {
                    problems.Add($"{Relative(root, project)}: PackageReference версии {match.Groups[1].Value} вместо 4.5.4");
                }
            }
        }

        Assert.That(problems, Is.Empty,
            "Сборка System.Threading.Tasks.Extensions 4.2.0.0 в папке приложения ломает привязку версий в дизайнере WF.");
    }

    [Test]
    public void AppConfig_RedirectsSystemThreadingTasksExtensionsToPinnedVersion()
    {
        // Redirect и закреплённый пакет обязаны совпадать: 4.5.4 даёт сборку 4.2.0.1.
        string config = ReadSource(@"AiRpaStudio\App.config");
        Match redirect = Regex.Match(
            config,
            @"<assemblyIdentity\s+name=""System\.Threading\.Tasks\.Extensions""[^>]*/>\s*<bindingRedirect\s+oldVersion=""[^""]*""\s+newVersion=""(?<version>[^""]+)""");

        Assert.That(redirect.Success, Is.True, "в App.config нет redirect'а для System.Threading.Tasks.Extensions");
        Assert.That(redirect.Groups["version"].Value, Is.EqualTo("4.2.0.1"));
    }

    [Test]
    public void OutputFolders_DoNotCarryOlderSystemThreadingTasksExtensions()
    {
        // Прямая проверка инцидента: папка приложения с копией 4.2.0.0 при redirect'е на 4.2.0.1
        // роняет ImportDesigner (FileLoadException). Отсутствующие папки (чистое дерево) пропускаем.
        string root = FindRepositoryRoot();
        string[] outputFolders =
        {
            @"AiRpaStudio\bin\Debug",
            @"AiRpaStudio\bin\Release",
            @"AiRpaStudio.Tests\bin\Debug\net48",
            @"AiRpaStudio.Tests\bin\Release\net48",
        };

        foreach (string folder in outputFolders)
        {
            string dll = Path.Combine(root, folder, "System.Threading.Tasks.Extensions.dll");
            if (!File.Exists(dll))
            {
                continue;
            }

            Version version = AssemblyName.GetAssemblyName(dll).Version;
            Assert.That(version, Is.EqualTo(new Version(4, 2, 0, 1)),
                $"{folder}: сборка {version} не совпадает с redirect'ом App.config (4.2.0.1)");
        }
    }

    // --- Инфраструктура. ---

    private static IEnumerable<string> EnumerateProjectFiles(string root, string pattern)
    {
        return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(path => !IsIgnored(root, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsIgnored(string root, string path)
    {
        string relative = Relative(root, path);
        string[] ignoredSegments = { "bin", "obj", "packages", ".git", ".vs", "tmp", "temp" };

        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => ignoredSegments.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    private static string Relative(string root, string path)
    {
        return path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"Нет файла {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "Не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }
}
