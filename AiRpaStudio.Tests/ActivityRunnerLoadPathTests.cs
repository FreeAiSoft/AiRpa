using System;
using System.Activities;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Agent.Core.Services;
using NUnit.Framework;
using RPA.Core;
using RPA.Entities;
using RPA.VsProject;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// Шаг 2 плана <c>LooseWorkflowRun</c>: новый путь загрузки прогона —
/// <see cref="ActivityRunner.LoadRunActivity(string)"/> и
/// <see cref="ActivityRunner.TryLoadFromBuiltAssembly"/>. WF-проект грузится из собранной сборки,
/// обычный робот — прежним XAML-путём, негатив «сборки нет» даёт причину от <c>RobotLocator</c>.
/// </summary>
[TestFixture]
public class ActivityRunnerLoadPathTests
{
    private readonly List<string> _tempDirectories = new List<string>();

    private const string SequenceXaml = @"<Activity xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities""
          xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Sequence DisplayName=""Empty sequence"" />
</Activity>";

    /// <summary>
    /// XAML загружается, но проверку WF не проходит: <c>Assign</c> без <c>To</c> и <c>Value</c>
    /// даёт две ошибки валидации — ошибка, невидимая загрузке (шаг 6 плана <c>LooseWorkflowRun</c>).
    /// </summary>
    private const string InvalidXaml = @"<Activity xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Sequence DisplayName=""Bad sequence"">
    <Assign DisplayName=""Assign without To and Value"" />
  </Sequence>
</Activity>";


    [TearDown]
    public void CleanupTempDirectories()
    {
        foreach (string directory in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
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

        _tempDirectories.Clear();
    }

    [Test]
    public void TryLoadFromBuiltAssembly_BuiltWorkflowProject_ReturnsTypeFromXClass_AndRuns()
    {
        BuiltWorkflowProject built = BuildShadowWorkflowProject();

        Activity activity = ActivityRunner.TryLoadFromBuiltAssembly(built.ProjectPath, built.MainXamlPath, out string reason);

        Assert.That(activity, Is.Not.Null, reason);
        Assert.That(reason, Is.Null.Or.Empty);
        Assert.That(activity.GetType().FullName, Is.EqualTo(built.TypeName),
            "тип обязан браться из x:Class Main.xaml, а не угадываться");

        WorkflowRunResult result = WorkflowRunEngine.Run(
            activity,
            new Dictionary<string, object> { { "InputText", "max" } },
            TimeSpan.FromSeconds(60));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Description);
            Assert.That(result.GetOutput("Result"), Is.EqualTo("MAX"),
                "активность из сборки обязана прогоняться, а не только загружаться");
        });
    }

    [Test]
    public void TryLoadFromBuiltAssembly_DoesNotLockBinDirectory()
    {
        BuiltWorkflowProject built = BuildShadowWorkflowProject();

        Activity activity = ActivityRunner.TryLoadFromBuiltAssembly(built.ProjectPath, built.MainXamlPath, out string reason);

        Assert.That(activity, Is.Not.Null, reason);
        Assert.That(FileLockDetector.IsFileLocked(built.AssemblyPath), Is.False,
            "оригинальная сборка в bin обязана оставаться незаблокированной после загрузки из памяти");
    }

    [Test]
    public void LoadRunActivity_RebuildAfterRun_DoesNotHitFileLock()
    {
        RequireMsBuild();
        BuiltWorkflowProject built = BuildShadowWorkflowProject();

        var project = new Project { IsWorkflowProject = true, ProjectFilePath = built.ProjectPath };

        // Первый прогон загружает робота (загрузка из памяти не блокирует bin).
        Activity first = ActivityRunner.LoadRunActivity(built.MainXamlPath, project, File.ReadAllText(built.MainXamlPath));
        Assert.That(first, Is.Not.Null);

        // Правка XAML без ручной сборки: ToUpper -> ToLower, файл новее dll.
        string updated = File.ReadAllText(built.MainXamlPath)
            .Replace("[InputText.ToUpper()]", "[InputText.ToLower()]");
        File.WriteAllText(built.MainXamlPath, updated);
        File.SetLastWriteTimeUtc(built.MainXamlPath, DateTime.UtcNow.AddMinutes(5));

        // Второй прогон обязан пересобрать проект (XAML новее dll) и не упасть на MSB3027 из-за блокировки.
        Activity second = ActivityRunner.LoadRunActivity(built.MainXamlPath, project, updated);

        Assert.That(second, Is.Not.Null,
            "автосборка обязана пройти, если первый прогон не блокирует bin");

        WorkflowRunResult result = WorkflowRunEngine.Run(
            second,
            new Dictionary<string, object> { { "InputText", "MAX" } },
            TimeSpan.FromSeconds(60));

        Assert.That(result.GetOutput("Result"), Is.EqualTo("max"),
            "прогон обязан идти по пересобранной версии XAML");
    }

    [Test]
    public void TryLoadFromBuiltAssembly_ProjectWithoutAssembly_ReturnsReason()
    {
        string csproj = CreateWorkflowProjectWithoutAssembly();

        Activity activity = ActivityRunner.TryLoadFromBuiltAssembly(csproj, IOPath.Combine(IOPath.GetDirectoryName(csproj), "Main.xaml"), out string reason);

        Assert.Multiple(() =>
        {
            Assert.That(activity, Is.Null);
            Assert.That(reason, Does.Contain("Соберите проект"),
                "причина обязана подсказывать, что делать");
            Assert.That(reason, Does.Contain("Robot.dll"),
                "причина обязана называть, какую сборку искали (RobotLocator)");
        });
    }

    [Test]
    public void LoadRunActivity_WorkflowProjectWithoutAssembly_ThrowsWithRobotLocatorReason()
    {
        string csproj = CreateWorkflowProjectWithoutAssembly();
        var project = new Project { IsWorkflowProject = true, ProjectFilePath = csproj };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => ActivityRunner.LoadRunActivity(IOPath.Combine(IOPath.GetDirectoryName(csproj), "Main.xaml"), project, null));

        Assert.Multiple(() =>
        {
            Assert.That(error.Message, Does.Contain("собранная сборка проекта"));
            Assert.That(error.Message, Does.Contain("Соберите проект"),
                "исключение обязано переносить причину от RobotLocator");
            Assert.That(error.Message, Does.Contain("Robot.dll"));
        });
    }

    [Test]
    public void LoadRunActivity_NonWorkflowProject_UsesXamlTextPath()
    {
        var project = new Project { IsWorkflowProject = false };

        Activity activity = ActivityRunner.LoadRunActivity("Main.xaml", project, SequenceXaml);

        Assert.That(activity, Is.InstanceOf<DynamicActivity>(),
            "не-WF project.json обязан грузиться прежним XAML-путём в DynamicActivity");
    }

    [Test]
    public void LoadRunActivity_MissingAssembly_AutoBuildsAndRuns()
    {
        RequireMsBuild();
        BuiltWorkflowProject built = PrepareShadowWorkflowProject();

        Assert.That(File.Exists(built.AssemblyPath), Is.False, "сборки ещё не должно быть");

        var project = new Project { IsWorkflowProject = true, ProjectFilePath = built.ProjectPath };
        Activity activity = ActivityRunner.LoadRunActivity(
            built.MainXamlPath, project, File.ReadAllText(built.MainXamlPath));

        Assert.That(activity, Is.Not.Null);
        Assert.That(activity.GetType().FullName, Is.EqualTo(built.TypeName),
            "автосборка обязана дать тип из x:Class Main.xaml");

        WorkflowRunResult result = WorkflowRunEngine.Run(
            activity,
            new Dictionary<string, object> { { "InputText", "max" } },
            TimeSpan.FromSeconds(60));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Description);
            Assert.That(result.GetOutput("Result"), Is.EqualTo("MAX"));
        });
    }

    [Test]
    public void LoadRunActivity_OutdatedAssembly_AutoRebuildsAndRunsUpdatedVersion()
    {
        RequireMsBuild();
        BuiltWorkflowProject built = BuildShadowWorkflowProject();

        // Правка XAML без ручной сборки: ToUpper -> ToLower, файл делаем новее dll.
        string updatedXaml = File.ReadAllText(built.MainXamlPath)
            .Replace("[InputText.ToUpper()]", "[InputText.ToLower()]");
        File.WriteAllText(built.MainXamlPath, updatedXaml);
        File.SetLastWriteTimeUtc(built.MainXamlPath, DateTime.UtcNow.AddMinutes(5));

        var project = new Project { IsWorkflowProject = true, ProjectFilePath = built.ProjectPath };
        Activity activity = ActivityRunner.LoadRunActivity(built.MainXamlPath, project, updatedXaml);

        Assert.That(activity, Is.Not.Null);

        WorkflowRunResult result = WorkflowRunEngine.Run(
            activity,
            new Dictionary<string, object> { { "InputText", "MAX" } },
            TimeSpan.FromSeconds(60));

        Assert.That(result.GetOutput("Result"), Is.EqualTo("max"),
            "прогон обязан идти по пересобранной версии XAML");
    }

    [Test]
    public void LoadRunActivity_WorkflowProject_LoadsTypeFromTargetXaml_NotMain()
    {
        BuiltWorkflowProject built = BuildShadowWorkflowProjectWithSubProcess(out string subXamlPath, out string subTypeName);

        var project = new Project { IsWorkflowProject = true, ProjectFilePath = built.ProjectPath };
        Activity activity = ActivityRunner.LoadRunActivity(subXamlPath, project, File.ReadAllText(subXamlPath));

        Assert.That(activity, Is.Not.Null);
        Assert.That(activity.GetType().FullName, Is.EqualTo(subTypeName),
            "прогон активной вкладки-подпроцесса обязан грузить тип из x:Class этого подпроцесса, а не Main.xaml");

        WorkflowRunResult result = WorkflowRunEngine.Run(
            activity,
            new Dictionary<string, object> { { "InputText", "max" } },
            TimeSpan.FromSeconds(60));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Description);
            Assert.That(result.GetOutput("Result"), Is.EqualTo("MAX"));
        });
    }


    [Test]
    public void TryLoadOrBuildFromProject_UnbuildableProject_ReportsBuildAndLocatorReasons()
    {
        string csproj = CreateWorkflowProjectWithoutAssembly();

        Activity activity = ActivityRunner.TryLoadOrBuildFromProject(csproj, IOPath.Combine(IOPath.GetDirectoryName(csproj), "Main.xaml"), out string reason);

        Assert.Multiple(() =>
        {
            Assert.That(activity, Is.Null);
            Assert.That(reason, Does.Contain("Автосборка перед запуском не прошла"),
                "неудачная автосборка обязана сообщать о себе");
            Assert.That(reason, Does.Contain("Соберите проект"),
                "причина RobotLocator обязана сохраняться и после автосборки");
            Assert.That(reason, Does.Contain("Robot.dll"));
        });
    }


    [Test]
    public void ValidateForRun_InvalidWorkflow_ThrowsWithClearMessage()
    {
        Activity activity = WorkflowRunEngine.LoadActivityFromText(InvalidXaml);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => ActivityRunner.ValidateForRun(activity));

        Assert.Multiple(() =>
        {
            Assert.That(error.Message, Does.Contain("проверка workflow"),
                "ошибки валидации обязаны быть названы");
            Assert.That(error.Message, Does.Contain("2"),
                "Assign без To/Value даёт две ошибки валидации");
        });
    }

    [Test]
    public void ValidateForRun_BuiltAssembly_DoesNotThrow()
    {
        BuiltWorkflowProject built = BuildShadowWorkflowProject();

        Activity activity = ActivityRunner.TryLoadOrBuildFromProject(built.ProjectPath, built.MainXamlPath, out string reason);

        Assert.That(activity, Is.Not.Null, reason);

        Assert.DoesNotThrow(() => ActivityRunner.ValidateForRun(activity),
            "валидный собранный робот обязан проходить проверку перед запуском");
    }

    [Test]
    public void NormalizeWorkflowXamlBeforeBuild_AddsIgnorableToDesignerXaml()
    {
        string dir = IOPath.Combine(IOPath.GetTempPath(), "activity_runner_norm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirectories.Add(dir);

        string workflowPath = IOPath.Combine(dir, "SubProcess3.xaml");
        string original = NonIgnorableDesignerXaml("RPA_1.SubProcess3");
        File.WriteAllText(workflowPath, original);

        string otherPath = IOPath.Combine(dir, "Theme.xaml");
        const string otherText = "<UserControl xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />";
        File.WriteAllText(otherPath, otherText);

        string binDir = IOPath.Combine(dir, "bin");
        Directory.CreateDirectory(binDir);
        string binXaml = IOPath.Combine(binDir, "SubProcess3.xaml");
        File.WriteAllText(binXaml, original);

        ActivityRunner.NormalizeWorkflowXamlBeforeBuild(dir);

        string normalized = File.ReadAllText(workflowPath);
        Assert.Multiple(() =>
        {
            Assert.That(normalized, Does.Contain("mc:Ignorable=\"sap sap2010\""),
                "presentation-атрибуты без mc:Ignorable ломают XamlBuildTask (XC1031)");
            Assert.That(normalized, Does.Contain("xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\""));
            Assert.That(normalized, Does.Contain("sap2010:ExpressionActivityEditor.ExpressionActivityEditor=\"C#\""),
                "нормализация не должна удалять presentation-атрибуты конструктора");
        });

        Assert.That(File.ReadAllText(otherPath), Is.EqualTo(otherText),
            "чужой XML (не рабочий процесс) не должен переписываться");
        Assert.That(File.ReadAllText(binXaml), Is.EqualTo(original),
            "каталоги bin/obj не должны трогаться нормализацией перед сборкой");
    }

    [Test]
    public void LoadRunActivity_NonIgnorableDesignerXaml_AutoBuildsAfterNormalization()
    {
        RequireMsBuild();
        BuiltWorkflowProject built = PrepareShadowWorkflowProject();

        string projectDir = IOPath.GetDirectoryName(built.ProjectPath);
        string rootNamespace = built.TypeName.Substring(0, built.TypeName.LastIndexOf('.'));
        string subTypeName = rootNamespace + ".SubProcess3";
        string subXamlPath = IOPath.Combine(projectDir, "SubProcess3.xaml");

        File.WriteAllText(subXamlPath, NonIgnorableDesignerXaml(subTypeName));

        File.WriteAllText(
            built.ProjectPath,
            File.ReadAllText(built.ProjectPath).Replace(
                "    <XamlAppDef Include=\"Main.xaml\">",
                "    <XamlAppDef Include=\"SubProcess3.xaml\">" + Environment.NewLine
                    + "      <SubType>Designer</SubType>" + Environment.NewLine
                    + "      <Generator>MSBuild:Compile</Generator>" + Environment.NewLine
                    + "    </XamlAppDef>" + Environment.NewLine
                    + "    <XamlAppDef Include=\"Main.xaml\">"));

        var project = new Project { IsWorkflowProject = true, ProjectFilePath = built.ProjectPath };
        Activity activity = ActivityRunner.LoadRunActivity(
            built.MainXamlPath, project, File.ReadAllText(built.MainXamlPath));

        Assert.That(activity, Is.Not.Null,
            "автосборка обязана пройти после нормализации XAML с presentation-атрибутами без mc:Ignorable");
        Assert.That(activity.GetType().FullName, Is.EqualTo(built.TypeName),
            "прогон обязан грузить тип из x:Class Main.xaml");

        Assert.That(File.ReadAllText(subXamlPath), Does.Contain("mc:Ignorable=\"sap sap2010\""),
            "нормализация обязана сохранить исправленный XAML на диск");
    }

    // --- Инфраструктура. ---

    private static string RepoRoot
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

    private static string Net472RobotSourceDir => IOPath.Combine(RepoRoot, "AiRpaStudio", "TestProcess", "Net472Robot");

    private static void RequireMsBuild()
    {
        MsBuildLocation location = VsProjectBuilder.FindMSBuild(MsBuildToolchain.Auto);

        if (!location.Found)
        {
            Assert.Ignore("MSBuild не найден на машине — автосборка эталона пропущена");
        }
    }

    private BuiltWorkflowProject PrepareShadowWorkflowProject()
    {
        string source = Net472RobotSourceDir;
        string target = IOPath.Combine(IOPath.GetTempPath(), "activity_runner_load_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);

        // Уникальные AssemblyName/RootNamespace/x:Class, чтобы несколько теневых сборок одного
        // эталона в одном тестовом процессе не «склеивались» по идентичности в LoadFrom-контексте.
        string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        string assemblyName = "Net472Robot_" + suffix;
        string typeName = assemblyName + ".Main";

        string csproj = IOPath.Combine(target, "Net472Robot.csproj");
        string mainXaml = IOPath.Combine(target, "Main.xaml");

        CopyProjectFile(source, target, "Net472Robot.csproj");
        CopyProjectFile(source, target, "Main.xaml");

        string propertiesSource = IOPath.Combine(source, "Properties");
        string propertiesTarget = IOPath.Combine(target, "Properties");
        Directory.CreateDirectory(propertiesTarget);
        CopyProjectFile(propertiesSource, propertiesTarget, "AssemblyInfo.cs");

        RewriteProjectIdentity(csproj, mainXaml, assemblyName, typeName);

        _tempDirectories.Add(target);

        BuiltWorkflowProject built = new BuiltWorkflowProject();
        built.ProjectPath = csproj;
        built.MainXamlPath = mainXaml;
        built.AssemblyPath = IOPath.Combine(target, "bin", "Debug", assemblyName + ".dll");
        built.TypeName = typeName;
        return built;
    }


    private BuiltWorkflowProject BuildShadowWorkflowProject()
    {
        MsBuildLocation location = VsProjectBuilder.FindMSBuild(MsBuildToolchain.Auto);

        if (!location.Found)
        {
            Assert.Ignore("MSBuild не найден на машине — сборка эталона пропущена");
        }

        string source = Net472RobotSourceDir;
        string target = IOPath.Combine(IOPath.GetTempPath(), "activity_runner_load_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);

        // Уникальные AssemblyName/RootNamespace/x:Class, чтобы несколько теневых сборок одного
        // эталона в одном тестовом процессе не «склеивались» по идентичности в LoadFrom-контексте.
        string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        string assemblyName = "Net472Robot_" + suffix;
        string typeName = assemblyName + ".Main";

        string csproj = IOPath.Combine(target, "Net472Robot.csproj");
        string mainXaml = IOPath.Combine(target, "Main.xaml");

        CopyProjectFile(source, target, "Net472Robot.csproj");
        CopyProjectFile(source, target, "Main.xaml");

        string propertiesSource = IOPath.Combine(source, "Properties");
        string propertiesTarget = IOPath.Combine(target, "Properties");
        Directory.CreateDirectory(propertiesTarget);
        CopyProjectFile(propertiesSource, propertiesTarget, "AssemblyInfo.cs");

        RewriteProjectIdentity(csproj, mainXaml, assemblyName, typeName);

        _tempDirectories.Add(target);

        var log = new List<string>();
        VsBuildResult result = VsProjectBuilder.BuildWithToolchain(
            csproj, "Debug", MsBuildToolchain.Auto, log.Add, restore: false);

        Assert.That(result.Success, Is.True,
            "сборка эталона в тени не прошла: " + Environment.NewLine
            + string.Join(Environment.NewLine, result.Errors.Take(10)) + Environment.NewLine + log.FirstOrDefault());

        return new BuiltWorkflowProject
        {
            ProjectPath = csproj,
            AssemblyPath = IOPath.Combine(target, "bin", "Debug", assemblyName + ".dll"),
            TypeName = typeName,
            MainXamlPath = mainXaml
        };
    }

    private BuiltWorkflowProject BuildShadowWorkflowProjectWithSubProcess(out string subXamlPath, out string subTypeName)
    {
        MsBuildLocation location = VsProjectBuilder.FindMSBuild(MsBuildToolchain.Auto);

        if (!location.Found)
        {
            Assert.Ignore("MSBuild не найден на машине — сборка эталона пропущена");
        }

        string source = Net472RobotSourceDir;
        string target = IOPath.Combine(IOPath.GetTempPath(), "activity_runner_sub_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);

        // Уникальные AssemblyName/RootNamespace/x:Class, чтобы несколько теневых сборок одного
        // эталона в одном тестовом процессе не «склеивались» по идентичности в LoadFrom-контексте.
        string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        string assemblyName = "Net472Robot_" + suffix;
        string mainTypeName = assemblyName + ".Main";
        subTypeName = assemblyName + ".SubProcess1";

        string csproj = IOPath.Combine(target, "Net472Robot.csproj");
        string mainXaml = IOPath.Combine(target, "Main.xaml");
        subXamlPath = IOPath.Combine(target, "SubProcess1.xaml");

        CopyProjectFile(source, target, "Net472Robot.csproj");
        CopyProjectFile(source, target, "Main.xaml");
        File.Copy(IOPath.Combine(source, "Main.xaml"), subXamlPath);

        string propertiesSource = IOPath.Combine(source, "Properties");
        string propertiesTarget = IOPath.Combine(target, "Properties");
        Directory.CreateDirectory(propertiesTarget);
        CopyProjectFile(propertiesSource, propertiesTarget, "AssemblyInfo.cs");

        RewriteProjectIdentity(csproj, mainXaml, assemblyName, mainTypeName);

        // SubProcess1.xaml — второй XAML-сценарий проекта: копия Main.xaml с собственным x:Class.
        File.WriteAllText(
            subXamlPath,
            File.ReadAllText(subXamlPath)
                .Replace("x:Class=\"Net472Robot.Main\"", "x:Class=\"" + subTypeName + "\"")
                .Replace("Main sequence", "Sub process"));

        // Добавляем SubProcess1.xaml в проект, чтобы MSBuild собрал его тип в ту же сборку.
        File.WriteAllText(
            csproj,
            File.ReadAllText(csproj).Replace(
                "    <XamlAppDef Include=\"Main.xaml\">",
                "    <XamlAppDef Include=\"SubProcess1.xaml\">" + Environment.NewLine
                    + "      <SubType>Designer</SubType>" + Environment.NewLine
                    + "      <Generator>MSBuild:Compile</Generator>" + Environment.NewLine
                    + "    </XamlAppDef>" + Environment.NewLine
                    + "    <XamlAppDef Include=\"Main.xaml\">"));

        _tempDirectories.Add(target);

        var log = new List<string>();
        VsBuildResult result = VsProjectBuilder.BuildWithToolchain(
            csproj, "Debug", MsBuildToolchain.Auto, log.Add, restore: false);

        Assert.That(result.Success, Is.True,
            "сборка эталона с SubProcess1 в тени не прошла: " + Environment.NewLine
            + string.Join(Environment.NewLine, result.Errors.Take(10)) + Environment.NewLine + log.FirstOrDefault());

        return new BuiltWorkflowProject
        {
            ProjectPath = csproj,
            AssemblyPath = IOPath.Combine(target, "bin", "Debug", assemblyName + ".dll"),
            TypeName = mainTypeName,
            MainXamlPath = mainXaml
        };
    }


    private static void CopyProjectFile(string sourceDir, string targetDir, string fileName)
    {
        File.Copy(IOPath.Combine(sourceDir, fileName), IOPath.Combine(targetDir, fileName), true);
    }

    private static void RewriteProjectIdentity(string csproj, string mainXaml, string assemblyName, string typeName)
    {
        File.WriteAllText(
            csproj,
            File.ReadAllText(csproj)
                .Replace("<AssemblyName>Net472Robot</AssemblyName>", "<AssemblyName>" + assemblyName + "</AssemblyName>")
                .Replace("<RootNamespace>Net472Robot</RootNamespace>", "<RootNamespace>" + assemblyName + "</RootNamespace>"));

        File.WriteAllText(
            mainXaml,
            File.ReadAllText(mainXaml)
                .Replace("x:Class=\"Net472Robot.Main\"", "x:Class=\"" + typeName + "\""));
    }

    private static string NonIgnorableDesignerXaml(string typeName)
    {
        return @"<Activity x:Class=""" + typeName + @""" sap2010:ExpressionActivityEditor.ExpressionActivityEditor=""C#"" sap:VirtualizedContainerService.HintSize=""440,440"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:sap=""http://schemas.microsoft.com/netfx/2009/xaml/activities/presentation"" xmlns:sap2010=""http://schemas.microsoft.com/netfx/2010/xaml/activities/presentation"" xmlns:sco=""clr-namespace:System.Collections.ObjectModel;assembly=mscorlib"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <TextExpression.NamespacesForImplementation>
    <sco:Collection x:TypeArguments=""x:String"">
      <x:String>System</x:String>
      <x:String>System.Collections.Generic</x:String>
      <x:String>System.Data</x:String>
      <x:String>System.Linq</x:String>
      <x:String>System.Text</x:String>
    </sco:Collection>
  </TextExpression.NamespacesForImplementation>
  <TextExpression.ReferencesForImplementation>
    <sco:Collection x:TypeArguments=""AssemblyReference"">
      <AssemblyReference>mscorlib</AssemblyReference>
      <AssemblyReference>System</AssemblyReference>
      <AssemblyReference>System.Core</AssemblyReference>
      <AssemblyReference>System.Data</AssemblyReference>
      <AssemblyReference>System.ServiceModel</AssemblyReference>
      <AssemblyReference>System.Xml</AssemblyReference>
    </sco:Collection>
  </TextExpression.ReferencesForImplementation>
</Activity>";
    }

    private string CreateWorkflowProjectWithoutAssembly()
    {
        string directory = IOPath.Combine(IOPath.GetTempPath(), "activity_runner_nodll_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        File.WriteAllText(
            IOPath.Combine(directory, "Robot.csproj"),
            @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <AssemblyName>Robot</AssemblyName>
    <RootNamespace>Robot</RootNamespace>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
  </PropertyGroup>
</Project>");

        File.WriteAllText(
            IOPath.Combine(directory, "Main.xaml"),
            @"<Activity x:Class=""Robot.Main"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <WriteLine Text=""робот"" />
</Activity>");

        _tempDirectories.Add(directory);

        return IOPath.Combine(directory, "Robot.csproj");
    }

    private sealed class BuiltWorkflowProject
    {
        public string ProjectPath;
        public string AssemblyPath;
        public string TypeName;
        public string MainXamlPath;
    }
}
