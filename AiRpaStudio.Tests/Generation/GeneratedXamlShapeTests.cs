using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Activities;
using System.Activities.XamlIntegration;
using AiRpaStudio.Tests.Design;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 2 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>: структурные стражи
/// сгенерированного XAML (по образцу <c>ThemeXamlTests</c>) и мутационный набор
/// «сгенерированный XAML ≠ сломанный».
/// <para>
/// Регекс-стражи дешёвые и ловят регресс канонизации («пропал xmlns:mca», «вернулся scg:List»),
/// мутационный набор доказывает, что гейт действительно проверяет эти элементы, а не пропускает всё.
/// Завершает набор прогон через настоящий конструктор (<see cref="DesignerBenchProxy"/>): только он
/// отвечает на вопрос «открывается ли файл на диаграмме».
/// </para>
/// </summary>
[TestFixture]
public class GeneratedXamlShapeTests
{
    private string _directory;
    private string _path;
    private string _xaml;

    [OneTimeSetUp]
    public void GenerateWorkflow()
    {
        _directory = WorkflowTestSamples.CreateTempDirectory();
        _path = Path.Combine(_directory, "Main.xaml");

        WorkflowBuildResult result = WorkflowTestSamples.BuildService().Save(WorkflowTestSamples.SimpleWorkflow(), _path);
        Assert.That(result.Ok, Is.True, "эталонный процесс не собрался." + Environment.NewLine + result.Describe());

        _xaml = result.Xaml;
    }

    [OneTimeTearDown]
    public void RemoveWorkflow()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }
        catch (Exception)
        {
            // Каталог мог остаться заблокированным — на результат прогона это не влияет.
        }
    }

    // --- Стражи формы: ровно то, что ожидает конструктор (п. 1.1). ---

    [Test]
    public void GeneratedXaml_HasRootActivityWithClass()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_xaml.TrimStart(), Does.StartWith("<Activity "),
                "корень файла студии — <Activity> с x:Class");
            Assert.That(_xaml, Does.Contain("x:Class=\"RPA_1.Main\""),
                "без x:Class дизайнер не связывает файл с типом проекта");
        });
    }

    [Test]
    public void GeneratedXaml_HasCSharpExpressionEditor()
    {
        Assert.That(Matches("sap2010:ExpressionActivityEditor\\.ExpressionActivityEditor=\"C#\""), Is.True,
            "без sap2010:ExpressionActivityEditor=\"C#\" выражения в конструкторе не редактируются");
    }

    [Test]
    public void GeneratedXaml_DeclaresCSharpExpressionNamespace()
    {
        Assert.That(Matches(
                "xmlns:mca=\"clr-namespace:Microsoft.CSharp.Activities;assembly=System.Activities\""), Is.True,
            "без xmlns:mca конструктор не понимает mca:CSharpValue/CSharpReference");
    }

    [Test]
    public void GeneratedXaml_HasTextExpressionSections()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_xaml, Does.Contain("<TextExpression.NamespacesForImplementation>"));
            Assert.That(_xaml, Does.Contain("<TextExpression.ReferencesForImplementation>"));
            Assert.That(_xaml, Does.Contain("sco:Collection x:TypeArguments=\"x:String\""),
                "коллекция пространств имён — как в эталоне (sco:Collection), а не scg:List с Capacity");
            Assert.That(_xaml, Does.Contain("sco:Collection x:TypeArguments=\"AssemblyReference\""));
            Assert.That(_xaml, Does.Not.Contain("scg:List"),
                "scg:List с атрибутом Capacity — шум сериализатора: эталон студии пишет sco:Collection");
        });
    }

    [Test]
    public void GeneratedXaml_HasProjectAssemblyReference()
    {
        Assert.That(_xaml, Does.Contain("<AssemblyReference>RPA-1</AssemblyReference>"),
            "без ссылки на сборку проекта конструктор не резолвит типы проекта");
    }

    [Test]
    public void GeneratedXaml_HasMarkupCompatibility()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_xaml, Does.Contain("xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\""));
            Assert.That(Matches("mc:Ignorable=\"sap sap2010\""), Is.True,
                "как в эталоне: presentation-пространства помечены необязательными");
        });
    }

    [Test]
    public void GeneratedXaml_HasIdRefOnRootAndActivities()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_xaml, Does.Contain("sap2010:WorkflowViewState.IdRef=\"RPA_1.Main_1\""),
                "у корня IdRef = «класс_1»");
            Assert.That(_xaml, Does.Contain("sap2010:WorkflowViewState.IdRef=\"Sequence_1\""));
            Assert.That(_xaml, Does.Contain("sap2010:WorkflowViewState.IdRef=\"Assign_1\""));
            Assert.That(_xaml, Does.Contain("sap2010:WorkflowViewState.IdRef=\"WriteLine_1\""));
        });
    }

    [Test]
    public void GeneratedXaml_HasNoExpressionLambdas()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_xaml, Does.Not.Contain("=>"),
                "лямбды не переживают сериализацию: только CSharpValue<T>/CSharpReference<T>");
            Assert.That(_xaml, Does.Not.Contain("DelegateInArgument"),
                "делегат в аргументе ломает и persistence, и отображение");
        });
    }

    [Test]
    public void GeneratedXaml_IsStyledLikeStudioFile()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_xaml, Does.Not.Contain("<?xml"),
                "файлы студии пишутся без XML-декларации");
            Assert.That(_xaml, Does.Contain("\r\n"), "переводы строк — CRLF, как в остальных файлах репозитория");
            Assert.That(_xaml, Does.Not.Contain("\r\n\r\n"), "пустых строк в каноническом файле нет");
        });
    }

    [Test]
    public void GeneratedXaml_IsUtf8WithoutBom()
    {
        byte[] bytes = File.ReadAllBytes(_path);

        Assert.Multiple(() =>
        {
            Assert.That(bytes.Length, Is.GreaterThan(3));
            Assert.That(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, Is.False,
                "BOM ломает сравнение XAML в тестах и diff в git");
        });
    }

    // --- Обратная загрузка и конструктор. ---

    [Test]
    public void GeneratedXaml_LoadsBackAsActivity()
    {
        Activity activity = ActivityXamlServices.Load(
            _path,
            new ActivityXamlServicesSettings { CompileExpressions = false });

        Assert.That(activity, Is.Not.Null, "файл обязан повторно загружаться как WF-активность");
    }

    /// <summary>
    /// Настоящая проверка контракта: сгенерированный файл открывается в rehosted-конструкторе,
    /// диаграмма построена, активностей видно столько же, сколько в модели, модальных окон нет.
    /// </summary>
    [Test]
    public void GeneratedXaml_OpensInDesigner()
    {
        DesignerBenchReport report = DesignerBenchProxy.Shared.Render(_path);

        Assert.That(report.Ok, Is.True,
            "сгенерированный XAML не открывается в конструкторе." + Environment.NewLine + report.Description);

        Assert.Multiple(() =>
        {
            Assert.That(report.UnrecognizedActivities, Is.Empty, "нераспознанных активностей быть не может");
            Assert.That(report.ValidationErrors, Is.Empty, "валидация WF в конструкторе обязана быть чистой");
            Assert.That(report.ModalWindows, Is.Empty, "модальных окон WF быть не может");
        });
    }

    // --- Мутационный набор: гейт обязан вернуть ошибку, а не «ок». ---

    [Test]
    public void MutatedGeneratedXaml_IsRejected()
    {
        var mutations = new (string Name, Func<string, string> Mutate)[]
        {
            ("без sap2010:ExpressionActivityEditor", xaml =>
                xaml.Replace(" sap2010:ExpressionActivityEditor.ExpressionActivityEditor=\"C#\"", string.Empty)),
            ("без x:Class", xaml => xaml.Replace(" x:Class=\"RPA_1.Main\"", string.Empty)),
            ("без xmlns:mca", xaml => xaml.Replace(
                " xmlns:mca=\"clr-namespace:Microsoft.CSharp.Activities;assembly=System.Activities\"", string.Empty)),
            ("чужой редактор выражений", xaml => xaml.Replace(
                "sap2010:ExpressionActivityEditor.ExpressionActivityEditor=\"C#\"",
                "sap2010:ExpressionActivityEditor.ExpressionActivityEditor=\"VB\"")),
            ("без IdRef корня", xaml => xaml.Replace(
                " sap2010:WorkflowViewState.IdRef=\"RPA_1.Main_1\"", string.Empty)),
            ("без mc:Ignorable", xaml => xaml.Replace(" mc:Ignorable=\"sap sap2010\"", string.Empty)),
            ("без TextExpression.NamespacesForImplementation", xaml => RemoveBlock(
                xaml, "<TextExpression.NamespacesForImplementation>", "</TextExpression.NamespacesForImplementation>")),
            ("без TextExpression.ReferencesForImplementation", xaml => RemoveBlock(
                xaml, "<TextExpression.ReferencesForImplementation>", "</TextExpression.ReferencesForImplementation>")),
            ("без ссылки на сборку проекта", xaml =>
                xaml.Replace("<AssemblyReference>RPA-1</AssemblyReference>", string.Empty)),
            ("неизвестная активность", xaml => xaml
                .Replace("<WriteLine", "<НесуществующаяАктивность")
                .Replace("</WriteLine>", "</НесуществующаяАктивность>")),
            ("битый XML", xaml => xaml.Substring(0, xaml.Length / 2))
        };

        var failures = new List<string>();

        foreach ((string name, Func<string, string> mutate) in mutations)
        {
            string mutated = mutate(_xaml);
            if (string.Equals(mutated, _xaml, StringComparison.Ordinal))
            {
                failures.Add($"{name}: мутация не изменила XAML — шаблон устарел, проверка ничего не значит");
                continue;
            }

            string path = Path.Combine(WorkflowTestSamples.CreateTempDirectory(), "Main.xaml");
            File.WriteAllText(path, mutated, new UTF8Encoding(false));

            WorkflowValidationReport report = WorkflowTestSamples.Gate().Validate(path);

            if (report.Ok)
            {
                failures.Add($"{name}: гейт принял сломанный XAML за валидный");
            }
        }

        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));

        // Контрольная точка мутационного набора: исходный XAML обязан проходить гейт,
        // иначе «все мутации отклонены» — следствие сломанного эталона, а не проверки.
        Assert.That(WorkflowTestSamples.Gate().Validate(_path).Ok, Is.True,
            "контрольный (немутированный) XAML обязан проходить гейт");
    }

    private bool Matches(string pattern)
    {
        return Regex.IsMatch(_xaml, pattern, RegexOptions.None, TimeSpan.FromSeconds(2));
    }

    private static string RemoveBlock(string xaml, string open, string close)
    {
        int start = xaml.IndexOf(open, StringComparison.Ordinal);
        if (start < 0)
        {
            return xaml;
        }

        int end = xaml.IndexOf(close, start, StringComparison.Ordinal);
        if (end < 0)
        {
            return xaml;
        }

        return xaml.Remove(start, end - start + close.Length);
    }
}
