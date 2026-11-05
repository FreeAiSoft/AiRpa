using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Agent.Core.Services;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RPA.VsProject;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// `TD-14` — страж пина .NET SDK (`AvalonEdit\global.json`).
/// <para>
/// Проблема: `AvalonEdit\global.json` пиннил SDK `10.0.100` с `rollForward: major`, из-за чего
/// MSBuild VS 2022 (17.13) получал SDK `10.0.401`, а тот требует MSBuild ≥ 18 — сборка
/// `ICSharpCode.AvalonEdit` падала `MSB4236` («не удалось найти пакет SDK
/// `Microsoft.NET.Sdk.WindowsDesktop`»). Это делало недостижимой строку DoD «0 ошибок из обеих VS».
/// </para>
/// <para>
/// Правило, которое держат эти тесты: пин SDK обязан быть <b>выполнимым для MSBuild VS 2022</b>
/// (major ≤ <see cref="MaxSdkMajorForVs2022"/>) и не иметь `rollForward`, переходящего через major
/// (`major`/`latestMajor`) — иначе пин снова «уедет» на 10.x. Плюс прямая проверка: проект
/// `ICSharpCode.AvalonEdit` реально собирается обоими тулчейнами без отката.
/// </para>
/// </summary>
[TestFixture]
public class SdkPinGuardTests
{
    /// <summary>
    /// Старший major .NET SDK, который ещё понимает MSBuild VS 2022 (17.13): SDK 10.x требует MSBuild ≥ 18.
    /// </summary>
    public const int MaxSdkMajorForVs2022 = 9;

    /// <summary>Значения `rollForward`, которые переходят через major и потому недопустимы в пине.</summary>
    private static readonly string[] MajorCrossingRollForward = { "major", "latestMajor" };

    private static readonly string[] SkippedDirectories =
    {
        "bin", "obj", "packages", ".git", ".vs", "node_modules", "temp", "tmp"
    };

    [Test]
    public void Repository_HasSingleSdkPin()
    {
        List<string> pins = FindSdkPins(RepositoryRoot);

        Assert.That(pins.Count, Is.EqualTo(1),
            "в репозитории должен быть ровно один `global.json` — иначе неясно, какой SDK выбирает dotnet/MSBuild, "
            + "и пин одного каталога незаметно конфликтует с другим. Найдено:"
            + Environment.NewLine + string.Join(Environment.NewLine, pins));

        Assert.That(Relative(pins[0]), Is.EqualTo("AvalonEdit/global.json"),
            "единственный пин SDK должен остаться там, где он был: `AvalonEdit\\global.json`");
    }

    [Test]
    public void PinnedSdk_DoesNotRequireNewerMsBuild()
    {
        PinInfo pin = ReadPin();

        Assert.That(pin.Version, Is.Not.Null,
            "в " + pin.RelativePath + " нет `sdk.version` — пин не задан");

        Assert.That(pin.Version.Major, Is.LessThanOrEqualTo(MaxSdkMajorForVs2022),
            "пин .NET SDK " + pin.Version + " (" + pin.RelativePath + ") требует MSBuild новее, чем есть в VS 2022 (17.13): "
            + "SDK 10.x падает с MSB4236 <Version 10.0.401 of the .NET SDK requires at least version 18.0.0 of MSBuild>, "
            + "и `ICSharpCode.AvalonEdit` перестаёт собираться из VS 2022. Допустимый major — не выше "
            + MaxSdkMajorForVs2022 + " (SDK 9.0.2xx — это ровно VS 17.13).");

        Assert.That(MajorCrossingRollForward, Does.Not.Contain(pin.RollForward ?? string.Empty),
            "`rollForward: " + pin.RollForward + "` в " + pin.RelativePath + " переходит через major: "
            + "при установленном SDK 10.0.401 MSBuild VS 2022 снова получит 10.x и упадёт MSB4236. "
            + "Допустимые значения — `latestPatch` / `minor` / `latestFeature` / `latestMinor` (не выходят за major).");
    }

    [Test]
    public void PinnedSdk_IsInstalledOnMachine()
    {
        PinInfo pin = ReadPin();

        List<Version> installed = GetInstalledSdkVersions();
        if (installed == null)
        {
            Assert.Ignore("CLI `dotnet` не найден — проверка установленного SDK пропущена");
        }

        Assert.That(installed, Is.Not.Empty, "`dotnet --list-sdks` вернул пустой список");

        // `rollForward` в допустимых значениях держится внутри major: подходит любой SDK того же major и не ниже пина.
        List<Version> matching = installed
            .Where(version => version.Major == pin.Version.Major && version >= pin.Version)
            .ToList();

        Assert.That(matching, Is.Not.Empty,
            "пин " + pin.Version + " невыполним на этой машине: установлены "
            + string.Join(", ", installed.Select(version => version.ToString()))
            + ". Либо установите SDK " + pin.Version.Major + ".0.x, либо измените " + pin.RelativePath + ".");
    }

    [TestCase(MsBuildToolchain.Vs2022)]
    [TestCase(MsBuildToolchain.Vs2026)]
    public void AvalonEdit_BuildsWithRequestedToolchain(MsBuildToolchain requested)
    {
        MsBuildLocation location = MsBuildLocator.Resolve(requested);
        if (!location.Found)
        {
            Assert.Ignore(MsBuildLocator.DescribeToolchain(requested) + " не установлен на машине — сборка пропущена");
        }

        var log = new List<string>();
        VsBuildResult result = VsProjectBuilder.BuildWithToolchain(AvalonEditProject, "Debug", requested, log.Add, restore: true);

        Assert.That(result.ToolchainFallback, Is.False,
            "запрошенный тулчейн не найден и подменён (IsFallback = true) — тест перестал бы проверять TD-14: " + location.Describe());

        Assert.That(result.RestoreSucceeded, Is.True,
            "restore упал: " + Environment.NewLine + (result.RestoreOutput ?? string.Empty));

        Assert.That(result.Success, Is.True,
            "`ICSharpCode.AvalonEdit` не собирается тулчейном " + MsBuildLocator.DescribeToolchain(requested)
            + " (" + result.ToolchainVersion + "):" + Environment.NewLine
            + string.Join(Environment.NewLine, result.Errors.Take(10)));

        Assert.That(File.Exists(IOPath.Combine(AvalonEditOutput, "ICSharpCode.AvalonEdit.dll")), Is.True,
            "после сборки нет ветки net462 — студия на 4.8 ссылается именно на неё");
    }

    private sealed class PinInfo
    {
        public string Path { get; set; }
        public string RelativePath { get; set; }
        public Version Version { get; set; }
        public string RollForward { get; set; }
    }

    private static PinInfo ReadPin()
    {
        List<string> pins = FindSdkPins(RepositoryRoot);

        Assert.That(pins, Is.Not.Empty, "в репозитории нет ни одного `global.json` — пин .NET SDK отсутствует");

        string path = pins[0];
        JObject document = JObject.Parse(File.ReadAllText(path));
        string rawVersion = (string)document["sdk"]?["version"];

        return new PinInfo
        {
            Path = path,
            RelativePath = Relative(path),
            Version = string.IsNullOrWhiteSpace(rawVersion) ? null : Version.Parse(rawVersion.Trim()),
            RollForward = ((string)document["sdk"]?["rollForward"])?.Trim().ToLowerInvariant()
        };
    }

    /// <summary>Все `global.json` репозитория, кроме выходных и служебных каталогов.</summary>
    private static List<string> FindSdkPins(string root)
    {
        var result = new List<string>();

        foreach (string file in Directory.GetFiles(root, "global.json", SearchOption.AllDirectories))
        {
            if (!IsSkipped(file, root))
            {
                result.Add(file);
            }
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);

        return result;
    }

    private static bool IsSkipped(string file, string root)
    {
        string relative = Relative(file, root);

        return relative
            .Split('/')
            .Take(relative.Split('/').Length - 1)
            .Any(part => SkippedDirectories.Contains(part, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Версии SDK из `dotnet --list-sdks`; null, если CLI недоступен.</summary>
    private static List<Version> GetInstalledSdkVersions()
    {
        string output = RunDotnet("--list-sdks");
        if (output == null)
        {
            return null;
        }

        var versions = new List<Version>();

        foreach (Match match in Regex.Matches(output, @"(?m)^\s*(\d+\.\d+\.\d+[^\s\[]*)"))
        {
            if (Version.TryParse(match.Groups[1].Value, out Version version))
            {
                versions.Add(version);
            }
        }

        return versions;
    }

    private static string RunDotnet(string arguments)
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = IOPath.GetTempPath()
            };

            using Process process = new Process { StartInfo = startInfo };

            if (!process.Start())
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(20000);

            return output;
        }
        catch (Exception)
        {
            // dotnet CLI нет на машине — вызывающий решает, Ignore это или падение.
            return null;
        }
    }

    private static string Relative(string path)
    {
        return Relative(path, RepositoryRoot);
    }

    private static string Relative(string path, string root)
    {
        string relative = path.Substring(root.Length).TrimStart(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar);

        return relative.Replace(IOPath.DirectorySeparatorChar, '/');
    }

    private static string RepositoryRoot
    {
        get
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

    private static string AvalonEditProject
    {
        get { return IOPath.Combine(RepositoryRoot, "AvalonEdit", "ICSharpCode.AvalonEdit", "ICSharpCode.AvalonEdit.csproj"); }
    }

    private static string AvalonEditOutput
    {
        get { return IOPath.Combine(RepositoryRoot, "AvalonEdit", "ICSharpCode.AvalonEdit", "bin", "Debug", "net462"); }
    }
}
