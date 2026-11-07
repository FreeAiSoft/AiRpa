using System.Text;
using AiRpaStudio.Tests.Design;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 4 конвейера генерации XAML на настоящем конструкторе:
/// палитра вставляет активность в живой <c>WorkflowDesigner</c> (стенд этапа 1), файл сохраняется
/// сериализатором конструктора, гейт валидации зелёный, вкладка открыта и активна.
/// <para>
/// Сценарий целиком выполняется в домене стенда (<see cref="DesignerBenchProxy"/>): конструктор нельзя
/// выносить за пределы STA-потока, на котором он создан.
/// </para>
/// </summary>
[TestFixture]
public class PaletteDesignerTests
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
    public void AddActivity_OnRealDesigner_ShowsActivityOpensTabAndValidates()
    {
        string path = WriteWorkflow();

        DesignerBenchPaletteReport report = DesignerBenchProxy.Shared.PaletteScenario(path);

        Assert.That(report.Ok, Is.True,
            "палитра не смогла вставить активность в настоящий конструктор." + Environment.NewLine + report.Description);

        Assert.Multiple(() =>
        {
            Assert.That(report.AddOk, Is.True, "вставка через ModelService обязана пройти гейт валидации");
            Assert.That(report.ActivityCountAfterAdd, Is.EqualTo(report.ActivityCountBefore + 1),
                "после add_activity в процессе на одну активность больше");
            Assert.That(report.ModelActivityCount, Is.GreaterThan(0),
                "активность обязана быть видна в дереве дизайнера: " + report.DesignerInfo);
            Assert.That(report.TabOpen, Is.True, "файл открыт вкладкой конструктора");
            Assert.That(report.TabActive, Is.True, "вкладка конструктора активна");
            Assert.That(report.SetArgumentOk, Is.True, "set_argument правит аргумент через модель вкладки");
            Assert.That(report.ExpressionInFile, Is.True, "новое выражение сохранено в файл процесса");
            Assert.That(report.WorkflowId, Is.EqualTo("RPA_1.Main"));
            Assert.That(report.ActivityId, Is.Not.Null.And.Not.Empty,
                "агент получает идентификатор добавленной активности");
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
