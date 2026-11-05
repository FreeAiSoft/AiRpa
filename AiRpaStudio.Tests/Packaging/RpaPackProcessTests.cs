using System;
using System.Diagnostics;
using System.IO;
using System.Text;

using NUnit.Framework;

using AiRpaStudio.Tests.Packages;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests.Packaging;

/// <summary>
/// `OR-03` — сквозная проверка настоящего <c>RpaPack.exe</c> отдельным процессом.
/// <para>
/// Упаковщик — инструмент скриптов сборки и агента: важно, что видит вызывающая сторона
/// (код возврата и вывод), а не только API. Здесь проверяется упаковка эталонного робота
/// без сборки (<c>--no-build</c>) и отказ на неверных аргументах и отсутствующем проекте.
/// </para>
/// </summary>
[TestFixture]
public class RpaPackProcessTests
{
    [Test]
    public void Exe_PacksReferenceRobot_ReturnsZeroAndCreatesPackage()
    {
        string exe = PackExecutable();

        if (!PackageFixtures.ReferenceRobotBuilt())
        {
            Assert.Ignore("эталон Net472Robot не собран: упаковщик пакует его готовый bin");
        }

        string directory = PackageFixtures.TempDirectory();
        string output = IOPath.Combine(directory, "robot.zip");

        ProcessResult run = Run(
            exe,
            "--project", PackageFixtures.ReferenceRobotProject(),
            "--configuration", "Debug",
            "--version", "2.0.0",
            "--no-build",
            "--out", output,
            "--json");

        Assert.Multiple(() =>
        {
            Assert.That(run.ExitCode, Is.EqualTo(0), "упаковка эталона обязана вернуть 0. stdout:" + Environment.NewLine + run.Output + run.Error);
            Assert.That(File.Exists(output), Is.True, "пакет обязан появиться на диске");
            Assert.That(run.Output, Does.Contain("\"status\":\"Created\""));
            Assert.That(run.Output, Does.Contain("\"version\":\"2.0.0\""));
            Assert.That(run.Output, Does.Contain("\"entryType\":\"Net472Robot.Main\""));
        });
    }

    [Test]
    public void Exe_NoArguments_ReturnsBadArgumentsAndHelp()
    {
        ProcessResult run = Run(PackExecutable());

        Assert.Multiple(() =>
        {
            Assert.That(run.ExitCode, Is.EqualTo(2), "пустая командная строка — ошибка аргументов");
            Assert.That(run.Error, Does.Contain("Ошибка аргументов"));
            Assert.That(run.Error, Does.Contain("--project"), "обязана печататься справка");
        });
    }

    [Test]
    public void Exe_MissingProject_ReturnsProjectInvalid()
    {
        string missing = IOPath.Combine(PackageFixtures.TempDirectory(), "нет.csproj");

        ProcessResult run = Run(PackExecutable(), "--project", missing, "--no-build", "--out", missing + ".zip");

        Assert.Multiple(() =>
        {
            Assert.That(run.ExitCode, Is.EqualTo(3), "отсутствующий проект — код 3. stdout:" + Environment.NewLine + run.Output + run.Error);
            Assert.That(run.Error, Does.Contain("проект робота не найден"));
        });
    }

    private static string PackExecutable()
    {
        string root = PackageFixtures.RepositoryRoot();

        foreach (string configuration in new[] { "Debug", "Release" })
        {
            string candidate = IOPath.Combine(root, "RpaPack", "bin", configuration, "net472", "RpaPack.exe");

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        Assert.Ignore("RpaPack.exe не собран: соберите решение (MSBuild AiRpa.sln) и повторите прогон тестов");

        return null;
    }

    private static ProcessResult Run(string exe, params string[] args)
    {
        var startInfo = new ProcessStartInfo(exe, BuildArguments(args))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = IOPath.GetDirectoryName(exe)
        };

        using (var process = new Process { StartInfo = startInfo })
        {
            process.Start();

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            Assert.That(process.WaitForExit(180_000), Is.True, "упаковщик не завершился за 3 минуты");

            return new ProcessResult(process.ExitCode, output, error);
        }
    }

    private static string BuildArguments(string[] args)
    {
        var text = new StringBuilder();

        foreach (string argument in args ?? new string[0])
        {
            if (text.Length > 0)
            {
                text.Append(' ');
            }

            text.Append(Quote(argument));
        }

        return text.ToString();
    }

    private static string Quote(string argument)
    {
        string value = argument ?? string.Empty;

        if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private readonly struct ProcessResult
    {
        public ProcessResult(int exitCode, string output, string error)
        {
            ExitCode = exitCode;
            Output = output ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public int ExitCode { get; }

        public string Output { get; }

        public string Error { get; }
    }
}
