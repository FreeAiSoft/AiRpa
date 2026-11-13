using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using ActivitiesTracker.Entities;
using Agent.Core.Services;
using NUnit.Framework;
using RPA.Core;
using RPA.Helpers;
using RPA.VsProject;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// `CP-08`, шаг E — 4.7.2-робот прогоняется штатным путём студии на 4.8-хосте.
/// <para>
/// Проверяется связка целиком, а не по частям: XAML эталона (`TestProcess\Net472Robot\Main.xaml`,
/// <c>x:Class="Net472Robot.Main"</c>) грузится общим движком <see cref="WorkflowRunEngine"/>,
/// а его сборка и зависимости находятся через <see cref="RobotAssemblyResolver"/> — тот самый
/// обработчик, который студия ставит в <c>ActivityRunner.Run</c> перед десериализацией XAML.
/// </para>
/// <para>
/// Без резолвера этот прогон падает на загрузке <c>Net472Robot</c>: сборка лежит в выходном каталоге
/// робота, а не в папке студии. Поэтому тест доказывает не «совместимость в принципе», а рабочий путь
/// «открыл проект 4.7.2 — запустил — получил результат».
/// </para>
/// </summary>
[TestFixture]
public class LegacyRobotStudioRunTests
{
    private const string RobotAssemblyName = "Net472Robot";

    private string shadowDirectory;

    [TearDown]
    public void UninstallResolver()
    {
        RobotAssemblyResolver.Uninstall();

        if (shadowDirectory == null)
        {
            return;
        }

        try
        {
            if (Directory.Exists(shadowDirectory))
            {
                Directory.Delete(shadowDirectory, true);
            }
        }
        catch (IOException)
        {
            // Загруженная в процесс сборка держит файл — каталог останется во временной папке.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Test]
    public void LegacyRobot_RunsThroughSharedEngineWithResolver()
    {
        BuildLegacyRobot();
        InstallResolverForRobotOutput();

        Assert.That(RobotAssemblyResolver.IsInstalled, Is.True, "обработчик поиска сборок робота обязан быть установлен");
        Assert.That(RobotAssemblyResolver.ProbeDirectories, Does.Contain(shadowDirectory),
            "каталог робота обязан попасть в каталоги поиска");

        System.Activities.Activity activity = WorkflowRunEngine.LoadActivityFromFile(LegacyRobotXaml);

        WorkflowRunResult result = WorkflowRunEngine.Run(
            activity,
            new Dictionary<string, object> { { "InputText", "max" } },
            TimeSpan.FromSeconds(120));

        Assert.That(result.IsSuccess, Is.True, result.Description);
        Assert.That(result.ExitCode, Is.EqualTo(0), "успешный прогон робота обязан давать код возврата 0");
        Assert.That(result.GetOutput("Result"), Is.EqualTo("MAX"), "робот 4.7.2 обязан вернуть Result = MAX");

        List<string> activities = result.TrackingEvents
            .Where(e => e.Record is System.Activities.Tracking.ActivityStateRecord)
            .Select(e => e.ActivityInfo?.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(activities, Does.Contain("Main sequence"), "нет трекинга Sequence");
            Assert.That(activities, Does.Contain("Has input"), "нет трекинга ветвления If");
            Assert.That(activities, Does.Contain("Guarded write"), "нет трекинга TryCatch");
            Assert.That(activities, Does.Contain("Log input"), "нет трекинга WriteLine");
            Assert.That(activities, Does.Contain("Build result"), "нет трекинга Assign");
        });
    }

    [Test]
    public void LegacyRobot_EmptyInput_TakesElseBranch()
    {
        BuildLegacyRobot();
        InstallResolverForRobotOutput();

        System.Activities.Activity activity = WorkflowRunEngine.LoadActivityFromFile(LegacyRobotXaml);

        WorkflowRunResult result = WorkflowRunEngine.Run(
            activity,
            new Dictionary<string, object> { { "InputText", string.Empty } },
            TimeSpan.FromSeconds(120));

        Assert.That(result.IsSuccess, Is.True, result.Description);
        Assert.That(result.GetOutput("Result"), Is.EqualTo(string.Empty),
            "пустой вход обязан дать пустой результат — проверяется ветка If.Else, а не только успешный путь");

        List<string> activities = result.TrackingEvents
            .Where(e => e.Record is System.Activities.Tracking.ActivityStateRecord)
            .Select(e => e.ActivityInfo?.Name)
            .ToList();

        Assert.That(activities, Does.Contain("Empty result"), "ветка If.Else обязана выполниться");
    }

    [Test]
    public void Resolver_FindsRobotAssemblyOnlyFromRobotDirectory()
    {
        BuildLegacyRobot();

        // Пустой каталог: сборку робота взять неоткуда — обработчик обязан промолчать (null),
        // а не подставить что-то из папки студии. Иначе тест выше доказывал бы совпадение, а не путь.
        string emptyDirectory = IOPath.Combine(IOPath.GetTempPath(), "empty_probe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyDirectory);

        try
        {
            RobotAssemblyResolver.Install(AppDomain.CurrentDomain, new[] { emptyDirectory });

            var request = new ResolveEventArgs("Net472Robot, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");

            Assert.That(RobotAssemblyResolver.ResolveAssembly(null, request), Is.Null,
                "в пустом каталоге сборки робота нет — обработчик обязан вернуть null");

            InstallResolverForRobotOutput();

            System.Reflection.Assembly found = RobotAssemblyResolver.ResolveAssembly(null, request);

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.Not.Null, "сборка робота обязана находиться в его выходном каталоге");
                Assert.That(found.GetName().Name, Is.EqualTo(RobotAssemblyName));

                // Сборка грузится из байтов (WorkflowRunEngine/RobotAssemblyResolver), поэтому
                // Location пуст — файловый блок для автосборки не ставится (MSB3021/MSB3027).
                Assert.That(FileLockDetector.IsFileLocked(IOPath.Combine(shadowDirectory, RobotAssemblyName + ".dll")),
                    Is.False, "резолвер обязан не блокировать сборку робота в его выходном каталоге");
            });
        }
        finally
        {
            Directory.Delete(emptyDirectory, true);
        }
    }

    // --- Инфраструктура. ---

    /// <summary>
    /// Копирует выход сборки робота во временный каталог и ставит на него резолвер: загруженная
    /// сборка блокирует `bin` робота, и следующая его сборка упала бы на MSB3027.
    /// </summary>
    private void InstallResolverForRobotOutput()
    {
        string source = IOPath.GetDirectoryName(LegacyRobotDll);

        shadowDirectory = IOPath.Combine(IOPath.GetTempPath(), "legacy_robot_run_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(shadowDirectory);

        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, IOPath.Combine(shadowDirectory, IOPath.GetFileName(file)), true);
        }

        RobotAssemblyResolver.Install(AppDomain.CurrentDomain, new[] { shadowDirectory });
    }

    private static void BuildLegacyRobot()
    {
        MsBuildLocation location = VsProjectBuilder.FindMSBuild(MsBuildToolchain.Auto);

        if (!location.Found)
        {
            Assert.Ignore("MSBuild не найден на машине — сборка эталона пропущена");
        }

        var log = new List<string>();
        VsBuildResult result = VsProjectBuilder.BuildWithToolchain(LegacyRobotProject, "Debug", MsBuildToolchain.Auto, log.Add, restore: true);

        Assert.That(result.Success, Is.True,
            "сборка эталона не прошла: " + Environment.NewLine
            + string.Join(Environment.NewLine, result.Errors.Take(10)) + Environment.NewLine + log.FirstOrDefault());
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

    private static string TestProcessRoot => IOPath.Combine(RepositoryRoot, "AiRpaStudio", "TestProcess");

    private static string LegacyRobotProject => IOPath.Combine(TestProcessRoot, RobotAssemblyName, RobotAssemblyName + ".csproj");

    private static string LegacyRobotDll => IOPath.Combine(TestProcessRoot, RobotAssemblyName, "bin", "Debug", RobotAssemblyName + ".dll");

    private static string LegacyRobotXaml => IOPath.Combine(TestProcessRoot, RobotAssemblyName, "Main.xaml");
}
