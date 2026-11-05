using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AiRpaStudio.Tests.Design;

namespace AiRpaStudio.Tests.Workflow;

/// <summary>
/// AG-07 на живом конструкторе: отказ писать в файл, открытый вкладкой, и запись с нормализацией +
/// настоящей валидацией студии в файл, который в конструкторе не открыт.
/// <para>
/// Проверяется только на настоящем <c>WorkflowDesigner</c>: «открыт в конструкторе» — это состояние
/// вкладки, а гейт валидации — WF-загрузка процесса. Фейковый мост доказал бы лишь то, что инструмент
/// умеет вызывать метод интерфейса.
/// </para>
/// </summary>
[TestFixture]
public class WorkflowXamlBridgeTests
{
    private static DesignerBenchXamlReport _report;
    private static string _workDirectory;

    [OneTimeSetUp]
    public void RunXamlScenarioOnLiveDesigner()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "AiRpaStudio-XamlBridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);

        string openPath = WriteWorkflow("Main.xaml");
        string unopenedPath = WriteWorkflow("NotOpened.xaml");

        _report = DesignerBenchProxy.Shared.WorkflowXamlScenario(openPath, unopenedPath);
    }

    [OneTimeTearDown]
    public void RemoveWorkDirectory()
    {
        try
        {
            if (_workDirectory != null && Directory.Exists(_workDirectory))
            {
                Directory.Delete(_workDirectory, true);
            }
        }
        catch (Exception)
        {
            // Каталог мог остаться заблокированным конструктором — на результат прогона это не влияет.
        }
    }

    [Test]
    public void Editor_RefusesWorkflowOpenInDesigner()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.Errors, Is.Empty, string.Join(Environment.NewLine, _report.Errors));
            Assert.That(_report.OpenInDesigner, Is.True, "файл обязан быть открыт вкладкой конструктора");
            Assert.That(_report.RefusedMessage, Does.Contain("File not written"));
            Assert.That(_report.RefusedMessage, Does.Contain("open in the studio designer"));
            Assert.That(_report.OpenFileUnchanged, Is.True, "файл, открытый конструктором, не переписывается текстом");
        });
    }

    [Test]
    public void Editor_WritesAndValidatesUnopenedWorkflow()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.Errors, Is.Empty, string.Join(Environment.NewLine, _report.Errors));
            Assert.That(_report.WriteMessage, Does.Contain("File written"));
            Assert.That(_report.WriteMessage, Does.Contain("validation:"), "валидация обязана выполниться");
            Assert.That(_report.WriteMessage, Does.Not.Contain("not performed"),
                "гейт студии подключён — это не null-object");
            Assert.That(_report.WrittenXaml, Does.Contain("xmlns:mc=\""), "на диск попадает нормализованный XAML");
            Assert.That(_report.WrittenXaml, Does.Contain("mc:Ignorable=\"sap sap2010\""));
        });
    }

    private static string WriteWorkflow(string fileName)
    {
        string source = Path.Combine(
            RepositoryRoot(),
            "AiRpaStudio.Tests",
            "Fixtures",
            "Workflow",
            "sequence.xaml");

        string target = Path.Combine(_workDirectory, fileName);
        File.WriteAllText(target, File.ReadAllText(source, Encoding.UTF8), new UTF8Encoding(false));

        return target;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new InvalidOperationException("не найден корень репозитория (AiRpa.sln)");
        }

        return directory.FullName;
    }
}

/// <summary>Сериализуемый отчёт сценария «текстовая правка XAML» (карточка <c>AG-07</c>).</summary>
[Serializable]
public sealed class DesignerBenchXamlReport
{
    public string OpenPath { get; set; }

    public string UnopenedPath { get; set; }

    public bool OpenInDesigner { get; set; }

    public string RefusedMessage { get; set; }

    public bool OpenFileUnchanged { get; set; }

    public string WriteMessage { get; set; }

    public string WrittenXaml { get; set; }

    public List<string> Errors { get; set; } = new List<string>();

    public bool Ok { get; set; }
}
