using System.Text;
using AiRpaStudio.Tests.Design;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 3 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c> на настоящем конструкторе:
/// <c>DesignerOpenService</c> открывает сгенерированный процесс в живом <c>WorkflowDesigner</c>
/// (стенд этапа 1), повторный вызов переиспользует вкладку, а генерация в открытый файл отклоняется.
/// <para>
/// Сценарий целиком выполняется в домене стенда (<see cref="DesignerBenchProxy"/>): конструктор нельзя
/// выносить за пределы STA-потока, на котором он создан, а его статические кэши нельзя смешивать с
/// кэшами тестов темы.
/// </para>
/// </summary>
[TestFixture]
public class DesignerOpenRealDesignerTests
{
    private readonly List<string> _temporaryDirectories = new List<string>();

    [TearDown]
    public void CleanupTemporaryDirectories()
    {
        foreach (string directory in _temporaryDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch (Exception)
            {
                // Каталог мог остаться заблокированным — на результат теста это не влияет.
            }
        }

        _temporaryDirectories.Clear();
    }

    [Test]
    public void OpenOrActivate_OpensGeneratedWorkflowInRealDesigner()
    {
        string path = WriteWorkflow();

        DesignerBenchOpenReport report = DesignerBenchProxy.Shared.OpenTabScenario(path);

        Assert.That(report.Ok, Is.True,
            "открытие сгенерированного процесса в настоящем конструкторе нарушено." + Environment.NewLine + report.Description);

        Assert.Multiple(() =>
        {
            Assert.That(report.WorkflowId, Is.EqualTo("RPA_1.Main"), "workflowId берётся из x:Class файла");
            Assert.That(report.ActivityCount, Is.GreaterThan(0), "в дереве дизайнера обязаны быть активности");
            Assert.That(report.OpenCount, Is.EqualTo(1), "на один файл — один конструктор");
            Assert.That(report.AlreadyOpenOnSecondCall, Is.True, "повторный вызов активирует существующую вкладку");
            Assert.That(report.SecondActivated, Is.True, "активной остаётся вкладка того же файла");
        });
    }

    /// <summary>
    /// Главное ограничение этапа 3: файл, открытый в конструкторе, нельзя перезаписать — студия держит
    /// его с <c>FileShare.Read</c>. Ответ обязан быть понятным (ошибка + подсказка), а не падением
    /// и не «тихой» потерей правок.
    /// </summary>
    [Test]
    public void GenerationIntoOpenFile_IsRejectedWithClearError()
    {
        string path = WriteWorkflow();
        string before = File.ReadAllText(path);

        DesignerBenchOpenReport report = DesignerBenchProxy.Shared.OpenTabScenario(path);

        Assert.Multiple(() =>
        {
            Assert.That(report.WriteBlocked, Is.True, "перезапись открытого файла обязана быть запрещена");
            Assert.That(report.WriteBlockedError, Does.Contain("открыт в конструкторе"));
            Assert.That(report.RegenerationOk, Is.False, "генерация в открытый файл не выполняется");
            Assert.That(report.RegenerationErrors, Is.Not.Empty, "агент получает ошибку, а не «ок»");
            Assert.That(File.ReadAllText(path), Is.EqualTo(before), "открытый файл не изменён");
        });
    }

    private string WriteWorkflow()
    {
        string directory = WorkflowTestSamples.CreateTempDirectory();
        _temporaryDirectories.Add(directory);

        string path = Path.Combine(directory, "Main.xaml");
        File.WriteAllText(path, WorkflowTestSamples.CanonicalXaml(), new UTF8Encoding(false));

        return path;
    }
}
