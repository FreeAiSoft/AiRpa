using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 2 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>: гейт валидации
/// <c>WorkflowValidationGate</c> и конвейер сохранения <c>WorkflowBuildService</c>.
/// <para>
/// Гейт считается работающим, только если он «зелёный» на каноническом XAML и «красный» на каждом
/// виде поломки: битый XML, неизвестная активность, активность без обязательных аргументов,
/// неканонический вид, отсутствие ссылки на сборку проекта. Иначе «валидный XAML» — это лишь
/// «не бросило исключение».
/// </para>
/// </summary>
[TestFixture]
public class WorkflowValidationGateTests
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
                // Каталог мог остаться заблокированным антивирусом — на результат теста это не влияет.
            }
        }

        _temporaryDirectories.Clear();
    }

    // --- Конвейер сохранения. ---

    [Test]
    public void Save_WritesCanonicalWorkflowThatPassesGate()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, "Main.xaml");

        WorkflowBuildResult result = WorkflowTestSamples.BuildService().Save(WorkflowTestSamples.SimpleWorkflow(), path);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True, "канонический процесс обязан проходить гейт." + Environment.NewLine + result.Describe());
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.WorkflowId, Is.EqualTo("RPA_1.Main"), "workflowId — это x:Class процесса");
            Assert.That(result.FilePath, Is.EqualTo(Path.GetFullPath(path)));
            Assert.That(File.Exists(path), Is.True, "успешная генерация обязана оставить файл");
        });

        WorkflowValidationReport report = WorkflowTestSamples.Gate().Validate(path);
        Assert.That(report.Ok, Is.True, report.Describe());
    }

    [Test]
    public void Build_DoesNotTouchDisk()
    {
        WorkflowBuildResult result = WorkflowTestSamples.BuildService().Build(WorkflowTestSamples.SimpleWorkflow());

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True, result.Describe());
            Assert.That(result.FilePath, Is.Null, "Build собирает XAML без сохранения");
            Assert.That(result.Xaml, Does.Contain("x:Class=\"RPA_1.Main\""));
        });
    }

    /// <summary>
    /// Невалидный XAML не должен оставаться в проекте: пишем во временный файл, валидируем,
    /// и только потом заменяем целевой (п. 4 плана).
    /// </summary>
    [Test]
    public void Save_KeepsTargetUntouchedWhenGateRejects()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, "Main.xaml");
        const string sentinel = "старое содержимое";
        File.WriteAllText(path, sentinel);

        var gate = new RejectingGate("wf.validation: сломано намеренно");
        var service = new WorkflowBuildService(WorkflowTestSamples.Options(), gate);

        WorkflowBuildResult result = service.Save(WorkflowTestSamples.SimpleWorkflow(), path);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False, "гейт вернул ошибку — генерация не может быть успешной");
            Assert.That(result.Errors, Does.Contain("wf.validation: сломано намеренно"));
            Assert.That(File.ReadAllText(path), Is.EqualTo(sentinel),
                "целевой файл перезаписывается только после успешного гейта");
            Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1),
                "временный файл обязан удаляться: мусор в папке проекта недопустим");
        });
    }

    [Test]
    public void Save_ReplacesExistingFile()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, "Main.xaml");
        File.WriteAllText(path, "старое содержимое");

        WorkflowBuildResult result = WorkflowTestSamples.BuildService().Save(WorkflowTestSamples.SimpleWorkflow(), path);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True, result.Describe());
            Assert.That(File.ReadAllText(path), Does.Contain("x:Class=\"RPA_1.Main\""));
            Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1), "временный файл обязан исчезнуть");
        });
    }

    /// <summary>
    /// Файл, открытый в конструкторе, студия держит с <c>FileShare.Read</c>: перезапись даёт
    /// <see cref="IOException"/>. Инструмент обязан вернуть понятную ошибку и подсказку, а не упасть.
    /// </summary>
    [Test]
    public void Save_ReportsFileOpenedInDesigner()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, "Main.xaml");
        WorkflowBuildResult initial = WorkflowTestSamples.BuildService().Save(WorkflowTestSamples.SimpleWorkflow(), path);
        Assert.That(initial.Ok, Is.True, initial.Describe());

        WorkflowBuildResult result;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = WorkflowTestSamples.BuildService().Save(WorkflowTestSamples.SimpleWorkflow(), path);
        }

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False, "перезапись занятого файла — ошибка инструмента, а не падение");
            Assert.That(result.Errors, Is.Not.Empty, "ошибка обязана объяснить, что не удалось заменить файл");
            Assert.That(result.Hints.Any(hint => hint.IndexOf("конструктор", StringComparison.OrdinalIgnoreCase) >= 0), Is.True,
                "подсказка обязана указать на открытую вкладку конструктора: " + string.Join(" | ", result.Hints));
            Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1), "временный файл обязан удаляться");
        });
    }

    [Test]
    public void Save_CreatesMissingDirectory()
    {
        string directory = Path.Combine(NewDirectory(), "Nested", "Project");
        string path = Path.Combine(directory, "Main.xaml");

        WorkflowBuildResult result = WorkflowTestSamples.BuildService().Save(WorkflowTestSamples.SimpleWorkflow(), path);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True, result.Describe());
            Assert.That(File.Exists(path), Is.True);
        });
    }

    [Test]
    public void Save_RejectsEmptyPath()
    {
        WorkflowBuildResult result = WorkflowTestSamples.BuildService().Save(WorkflowTestSamples.SimpleWorkflow(), " ");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Errors, Is.Not.Empty);
        });
    }

    // --- Гейт: обязательные проверки. ---

    [Test]
    public void Validate_MissingFile_ReturnsError()
    {
        string path = Path.Combine(NewDirectory(), "НетТакого.xaml");

        WorkflowValidationReport report = WorkflowTestSamples.Gate().Validate(path);

        Assert.Multiple(() =>
        {
            Assert.That(report.Ok, Is.False);
            Assert.That(report.Errors.Any(error => error.StartsWith("xaml.path", StringComparison.Ordinal)), Is.True,
                "гейт обязан вернуть ошибку, а не бросить исключение: " + report.Describe());
        });
    }

    [Test]
    public void Validate_MalformedXml_ReturnsError()
    {
        string path = WorkflowTestSamples.WriteTempXaml("<Activity xmlns=\"http://schemas.microsoft.com/netfx/2009/xaml/activities\">");

        WorkflowValidationReport report = WorkflowTestSamples.Gate().Validate(path);

        Assert.Multiple(() =>
        {
            Assert.That(report.Ok, Is.False);
            Assert.That(report.Errors.Any(error => error.StartsWith("xaml.malformed", StringComparison.Ordinal)), Is.True,
                report.Describe());
        });
    }

    /// <summary>
    /// WF не считает неизвестный тип ошибкой загрузки (подставляет ErrorActivity), поэтому проверка
    /// «нераспознанных активностей нет» обязана ловиться валидацией WF.
    /// </summary>
    [Test]
    public void Validate_UnknownActivityType_ReturnsError()
    {
        string xaml = WorkflowTestSamples.CanonicalXaml()
            .Replace("<WriteLine", "<НесуществующаяАктивность")
            .Replace("</WriteLine>", "</НесуществующаяАктивность>");

        string path = WorkflowTestSamples.WriteTempXaml(xaml);
        WorkflowValidationReport report = WorkflowTestSamples.Gate().Validate(path);

        Assert.That(report.Ok, Is.False, "XAML с неизвестным типом не может проходить гейт: " + report.Describe());
    }

    [Test]
    public void Validate_ActivityWithoutRequiredArguments_ReturnsError()
    {
        string xaml = WorkflowTestSamples.CanonicalXaml();

        int start = xaml.IndexOf("<Assign", StringComparison.Ordinal);
        int end = xaml.IndexOf("</Assign>", StringComparison.Ordinal);
        xaml = xaml.Substring(0, start)
               + "<Assign sap2010:WorkflowViewState.IdRef=\"Assign_1\" DisplayName=\"Без аргументов\" />"
               + xaml.Substring(end + "</Assign>".Length);

        string path = WorkflowTestSamples.WriteTempXaml(xaml);
        WorkflowValidationReport report = WorkflowTestSamples.Gate().Validate(path);

        Assert.That(report.Ok, Is.False, "Assign без To/Value обязан дать ошибку валидации: " + report.Describe());
    }

    /// <summary>
    /// Канонический вид проверяется отдельно от WF: без <c>x:Class</c> и редактора выражений файл
    /// загружается, но в конструкторе не работает. Флаг <c>RequireCanonicalShape</c> позволяет
    /// проверить это, не отключая WF-валидацию.
    /// </summary>
    [Test]
    public void Validate_ShapeChecksAreSeparateFromWfValidation()
    {
        const string minimal =
            "<Activity xmlns=\"http://schemas.microsoft.com/netfx/2009/xaml/activities\">" +
            "<Sequence><WriteLine Text=\"привет\" /></Sequence></Activity>";

        string path = WorkflowTestSamples.WriteTempXaml(minimal);

        WorkflowValidationReport strict = WorkflowTestSamples.Gate().Validate(path);
        WorkflowValidationReport lenient = new WorkflowValidationGate(new WorkflowValidationOptions
        {
            RequireCanonicalShape = false
        }).Validate(path);

        Assert.Multiple(() =>
        {
            Assert.That(strict.Ok, Is.False, "без канонического вида гейт обязан ругаться: " + strict.Describe());
            Assert.That(strict.Errors.Any(error => error.StartsWith("shape.", StringComparison.Ordinal)), Is.True,
                "ошибки канонического вида имеют код shape.*: " + strict.Describe());
            Assert.That(strict.Hints, Is.Not.Empty, "к ошибкам формы обязательна подсказка, как исправить");
            Assert.That(lenient.Ok, Is.True,
                "минимальный XAML валиден для WF — значит проверки формы работают отдельно, а не «всё красное»: " +
                lenient.Describe());
        });
    }

    [Test]
    public void Validate_MissingProjectAssemblyReference_ReturnsError()
    {
        string xaml = WorkflowTestSamples.CanonicalXaml()
            .Replace("      <AssemblyReference>RPA-1</AssemblyReference>" + Environment.NewLine, string.Empty)
            .Replace("      <AssemblyReference>RPA-1</AssemblyReference>", string.Empty);

        string path = WorkflowTestSamples.WriteTempXaml(xaml);
        WorkflowValidationReport report = WorkflowTestSamples.Gate().Validate(path);

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Not.Contain("<AssemblyReference>RPA-1</AssemblyReference>"),
                "мутация не сработала — проверьте формат XAML");
            Assert.That(report.Ok, Is.False, report.Describe());
            Assert.That(report.Errors.Any(error => error.StartsWith("shape.projectReference", StringComparison.Ordinal)),
                Is.True, "отсутствие ссылки на сборку проекта — отдельная ошибка: " + report.Describe());
        });
    }

    [Test]
    public void Validate_ReturnsWarningsWithoutFailing()
    {
        // Предупреждения не блокируют открытие: отчёт обязан их вернуть, но остаться зелёным.
        WorkflowValidationReport report = WorkflowTestSamples.Gate().Validate(
            WorkflowTestSamples.WriteTempXaml(WorkflowTestSamples.CanonicalXaml()));

        Assert.Multiple(() =>
        {
            Assert.That(report.Ok, Is.True, report.Describe());
            Assert.That(report.Warnings, Is.Not.Null, "список предупреждений есть всегда (может быть пустым)");
        });
    }

    private string NewDirectory()
    {
        string directory = WorkflowTestSamples.CreateTempDirectory();
        _temporaryDirectories.Add(directory);

        return directory;
    }

    /// <summary>Гейт-заглушка: возвращает заданную ошибку. Нужен, чтобы проверить откат файла.</summary>
    private sealed class RejectingGate : IWorkflowValidationGate
    {
        private readonly string _error;

        internal RejectingGate(string error)
        {
            _error = error;
        }

        public WorkflowValidationReport Validate(string xamlPath)
        {
            var report = new WorkflowValidationReport();
            report.AddError(string.Empty, _error);

            return report;
        }
    }
}
