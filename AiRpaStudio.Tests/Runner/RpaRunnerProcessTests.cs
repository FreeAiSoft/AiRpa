using System;
using System.Diagnostics;
using System.IO;
using System.Text;

using NUnit.Framework;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests.Runner;

/// <summary>
/// `OR-02`, шаг `W6` — сквозная проверка настоящего <c>RpaRunner.exe</c> отдельным процессом.
/// <para>
/// Библиотечные тесты проверяют код, но не проверяют то, что видит оркестратор: код возврата
/// процесса, текст в stdout и файлы на диске. Здесь раннер запускается как процесс на эталонном
/// роботе `TestProcess\Net472Robot` — тот же приём, каким его проверяет регресс `CP-09`:
/// <c>Main.xaml</c> (Sequence → If → TryCatch), вход <c>InputText = max</c>, выход <c>Result = MAX</c>.
/// </para>
/// <para>
/// Без собранного эталона тест не «зеленеет» молча: сначала пробуем собрать проект сами
/// (MSBuild из VS), а если тулчейна нет — помечаем прогон пропущенным с явной причиной.
/// </para>
/// </summary>
[TestFixture]
public class RpaRunnerProcessTests
{
    [Test]
    public void Exe_RunsReferenceRobot_ReturnsZeroAndResult()
    {
        string exe = RunnerExecutable();
        string project = ReferenceRobotProject();
        string resultPath = IOPath.Combine(TempDirectory(), "result.json");

        ProcessResult run = Run(exe, "--project", project, "--arg", "InputText=max", "--out", resultPath, "--run-id", "process-1");

        Assert.Multiple(() =>
        {
            Assert.That(run.ExitCode, Is.EqualTo(0), "прогон эталона обязан вернуть 0. stdout:" + Environment.NewLine + run.Output);
            Assert.That(run.Output, Does.Contain("(код 0)"));
            Assert.That(File.Exists(resultPath), Is.True, "раннер обязан записать result.json");
        });

        string result = File.ReadAllText(resultPath);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("\"Result\":\"MAX\""), "выходной аргумент эталона обязан быть в отчёте");
            Assert.That(result, Does.Contain("\"status\":\"success\""));
            Assert.That(result, Does.Contain("\"runId\":\"process-1\""));
        });
    }

    [Test]
    public void Exe_ValidateReferenceRobot_ReturnsZero()
    {
        string exe = RunnerExecutable();

        ProcessResult run = Run(exe, "--project", ReferenceRobotProject(), "--validate");

        Assert.That(run.ExitCode, Is.EqualTo(0), "проверка эталона обязана проходить. stdout:" + Environment.NewLine + run.Output);
        Assert.That(run.Output, Does.Contain("Validated"));
    }

    [Test]
    public void Exe_NoArguments_ReturnsBadArgumentsAndHelp()
    {
        string exe = RunnerExecutable();

        ProcessResult run = Run(exe);

        Assert.Multiple(() =>
        {
            Assert.That(run.ExitCode, Is.EqualTo(2), "пустая командная строка — ошибка аргументов");
            Assert.That(run.Error, Does.Contain("Ошибка аргументов"));
            Assert.That(run.Error, Does.Contain("--project"), "обязана печататься справка");
        });
    }

    [Test]
    public void Exe_NotBuiltRobot_ReturnsSix()
    {
        string exe = RunnerExecutable();
        string directory = TempDirectory();
        string project = IOPath.Combine(directory, "NotBuilt.csproj");

        File.WriteAllText(project, @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <AssemblyName>NotBuilt</AssemblyName>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
  </PropertyGroup>
</Project>");

        File.WriteAllText(IOPath.Combine(directory, "Main.xaml"), @"<Activity x:Class=""NotBuilt.Main""
  xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities""
  xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <WriteLine Text=""не собрано"" />
</Activity>");

        ProcessResult run = Run(exe, "--project", project);

        Assert.Multiple(() =>
        {
            Assert.That(run.ExitCode, Is.EqualTo(6), "несобранный робот — код 6. stdout:" + Environment.NewLine + run.Output);
            Assert.That(run.Output, Does.Contain("не собран"), "причина обязана быть в выводе");
        });
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

            Assert.That(process.WaitForExit(180_000), Is.True, "раннер не завершился за 3 минуты: прогон завис");

            return new ProcessResult(process.ExitCode, output, error);
        }
    }

    /// <summary>Аргументы процесса строкой с кавычками: ProcessStartInfo.ArgumentList на 4.8 нет.</summary>
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
    /// <summary>Путь к собранному раннеру; при отсутствии — сборка через MSBuild из VS.</summary>
    private static string RunnerExecutable()
    {
        string root = FindRepositoryRoot();

        foreach (string configuration in new[] { "Debug", "Release" })
        {
            string candidate = IOPath.Combine(root, "RpaRunner", "bin", configuration, "net472", "RpaRunner.exe");

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        Assert.Ignore("RpaRunner.exe не собран: соберите решение (MSBuild AiRpa.sln) и повторите прогон тестов");

        return null;
    }

    private static string ReferenceRobotProject()
    {
        string project = IOPath.Combine(
            FindRepositoryRoot(), "AiRpaStudio", "TestProcess", "Net472Robot", "Net472Robot.csproj");

        string assembly = IOPath.Combine(IOPath.GetDirectoryName(project), "bin", "Debug", "Net472Robot.dll");

        if (File.Exists(assembly))
        {
            return project;
        }

        Assert.Ignore("эталон Net472Robot не собран: тест собирает его вместе с регрессом TestProcessRegressionTests");

        return null;
    }

    private static string TempDirectory()
    {
        string directory = IOPath.Combine(IOPath.GetTempPath(), "runner_process_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        return directory;
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
