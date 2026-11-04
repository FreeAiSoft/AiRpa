using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using AiRpaStudio.Tests.Design;

namespace AiRpaStudio.Tests;

/// <summary>
/// Этап 1 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>: тестовый стенд конструктора.
/// <para>
/// Стенд считается рабочим, только если он «зелёный» на эталоне (<c>RPA-1\Main.xaml</c>) и умеет
/// говорить «нет»: неизвестный тип обязан попасть в отчёт как нераспознанная активность, битый XML —
/// дать отчёт с объяснением, воркфлоу с ошибкой валидации — непустой список ошибок. Иначе проверки
/// следующих этапов (канонический XAML, инструменты палитры) ничего не значат.
/// </para>
/// <para>
/// Прогон идёт через <see cref="DesignerBenchProxy"/> в отдельном домене приложения: отрисовка
/// дизайнера заполняет статические кэши WF объектами своего потока, и без изоляции падают тесты темы
/// панели «Активности» (подробности — в комментарии к мосту).
/// </para>
/// </summary>
[TestFixture]
public class DesignerTestHostTests
{
    private static DesignerBenchReport _reference;

    [OneTimeSetUp]
    public void RenderReferenceWorkflow()
    {
        _reference = DesignerBenchProxy.Shared.Render(ReferenceWorkflowPath());
    }

    // --- Стенд. ---

    [Test]
    public void Host_RunsOnStaThread()
    {
        DesignerBenchHostInfo host = DesignerBenchProxy.Shared.HostInfo();

        Assert.Multiple(() =>
        {
            Assert.That(host.ApartmentState, Is.EqualTo(ApartmentState.STA.ToString()),
                "конструктор WF работает только на STA-потоке");
            Assert.That(host.HostThreadId, Is.EqualTo(host.KnownHostThreadId),
                "вызов обязан выполняться на потоке стенда");
            Assert.That(host.IsAlive, Is.True, "поток стенда обязан жить между вызовами");
        });
    }

    [Test]
    public void Host_MarshalsCallsToItsOwnThread()
    {
        DesignerBenchHostInfo host = DesignerBenchProxy.Shared.HostInfo();

        Assert.That(host.HostThreadId, Is.Not.EqualTo(host.CallerThreadId),
            "без маршалинга дизайнер создавался бы в потоке теста и падал с исключением WPF о чужом потоке");
    }

    /// <summary>
    /// Страж модальных окон обязан их видеть — иначе тест «модальных окон не было» ничего не проверяет.
    /// </summary>
    [Test]
    public void ModalWindowGuard_ReportsWindow()
    {
        const string title = "Страж модальных окон";

        IReadOnlyList<string> windows = DesignerBenchProxy.Shared.WindowsSeenWhileShowing(title);

        Assert.That(windows, Has.Count.EqualTo(1), "страж не заметил показанное окно");
        Assert.That(windows[0], Does.Contain(title),
            "в отчёте обязан быть заголовок окна — по нему видно, чей это диалог");
    }

    // --- Эталон: стенд обязан быть зелёным на заведомо хорошем файле. ---

    [Test]
    public void ReferenceWorkflow_Loads()
    {
        Assert.That(_reference.LoadSucceeded, Is.True,
            "эталон RPA-1\\Main.xaml не загрузился в дизайнер." + Environment.NewLine + _reference.Description);
    }

    [Test]
    public void ReferenceWorkflow_RendersActivities()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_reference.SurfaceReady, Is.True,
                "поверхность DesignerView не построена — в студии это «пустая» диаграмма." +
                Environment.NewLine + _reference.Description);
            Assert.That(_reference.RenderedActivities, Is.Not.Empty,
                "в дереве конструктора нет ни одной активности." + Environment.NewLine + _reference.Description);
        });
    }

    [Test]
    public void ReferenceWorkflow_IsValid()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_reference.ValidationErrors, Is.Empty,
                "валидация WF нашла ошибки в эталоне." + Environment.NewLine + _reference.Description);
            Assert.That(_reference.DesignerValidationErrors, Is.Empty,
                "сам дизайнер нашёл ошибки в эталоне." + Environment.NewLine + _reference.Description);
            Assert.That(_reference.UnrecognizedActivities, Is.Empty,
                "в эталоне появились нераспознанные активности." + Environment.NewLine + _reference.Description);
        });

        TestContext.Out.WriteLine(_reference.Description);
    }

    [Test]
    public void ReferenceWorkflow_ShowsNoModalWindows()
    {
        Assert.That(_reference.ModalWindows, Is.Empty,
            "во время открытия эталона появилось модальное окно: студия в этот момент заблокирована." +
            Environment.NewLine + _reference.Description);
    }

    [Test]
    public void ReferenceWorkflow_MatchesTheWholeContract()
    {
        Assert.That(_reference.Ok, Is.True,
            "эталон не проходит контракт «валидный XAML сразу открывается в конструкторе»." +
            Environment.NewLine + _reference.Description);
    }

    // --- Стенд обязан говорить «нет». ---

    /// <summary>
    /// Неизвестный тип WF не считает ошибкой загрузки: он подставляет активность-заглушку
    /// «ErrorActivity», и без отдельной проверки сломанный XAML выглядел бы открытым.
    /// </summary>
    [Test]
    public void UnknownActivityType_IsReportedAsUnrecognized()
    {
        string path = WriteTempWorkflow(
            "<Activity xmlns=\"http://schemas.microsoft.com/netfx/2009/xaml/activities\">" +
            "<Sequence><НесуществующаяАктивность /></Sequence></Activity>");

        try
        {
            DesignerBenchReport report = DesignerBenchProxy.Shared.Render(path);

            Assert.Multiple(() =>
            {
                Assert.That(report.UnrecognizedActivities, Is.Not.Empty,
                    "неизвестный тип обязан попасть в отчёт как нераспознанная активность." +
                    Environment.NewLine + report.Description);
                Assert.That(report.ValidationErrors, Is.Not.Empty,
                    "XAML с неизвестным типом не должен проходить валидацию WF." +
                    Environment.NewLine + report.Description);
                Assert.That(report.Ok, Is.False,
                    "XAML с неизвестным типом не может проходить контракт." + Environment.NewLine + report.Description);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Битый XML дизайнер тоже переживает (показывает повреждённый документ), поэтому контракт нарушается
    /// не исключением, а отчётом: <see cref="DesignerBenchReport.Ok"/> = <c>false</c> и обязательное
    /// объяснение причины.
    /// </summary>
    [Test]
    public void UnparsableXaml_IsReportedInsteadOfBeingAccepted()
    {
        string path = WriteTempWorkflow("<Activity xmlns=\"http://schemas.microsoft.com/netfx/2009/xaml/activities\">");

        try
        {
            DesignerBenchReport report = DesignerBenchProxy.Shared.Render(path);

            bool explained = !string.IsNullOrWhiteSpace(report.LoadError)
                             || report.UnrecognizedActivities.Count > 0
                             || report.ValidationErrors.Count > 0
                             || report.DesignerValidationErrors.Count > 0
                             || report.Hints.Count > 0;

            Assert.Multiple(() =>
            {
                Assert.That(report.Ok, Is.False,
                    "битый XML не может проходить контракт «валидный XAML»." + Environment.NewLine + report.Description);
                Assert.That(explained, Is.True,
                    "отчёт обязан объяснить, чем плох файл: пустой отчёт хуже падения." +
                    Environment.NewLine + report.Description);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Воркфлоу с ошибкой валидации: <c>Assign</c> без обязательных <c>To</c>/<c>Value</c>.
    /// Проверка нужна, чтобы «нет ошибок у эталона» не оказалось следствием неработающей валидации.
    /// </summary>
    [Test]
    public void WorkflowWithoutRequiredArguments_IsRejected()
    {
        string path = WriteTempWorkflow(
            "<Activity mc:Ignorable=\"sap sap2010\" sap2010:ExpressionActivityEditor.ExpressionActivityEditor=\"C#\"" + Environment.NewLine +
            " xmlns=\"http://schemas.microsoft.com/netfx/2009/xaml/activities\"" + Environment.NewLine +
            " xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"" + Environment.NewLine +
            " xmlns:mca=\"clr-namespace:Microsoft.CSharp.Activities;assembly=System.Activities\"" + Environment.NewLine +
            " xmlns:sap=\"http://schemas.microsoft.com/netfx/2009/xaml/activities/presentation\"" + Environment.NewLine +
            " xmlns:sap2010=\"http://schemas.microsoft.com/netfx/2010/xaml/activities/presentation\"" + Environment.NewLine +
            " xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" + Environment.NewLine +
            "  <Sequence sap2010:WorkflowViewState.IdRef=\"Sequence_1\">" + Environment.NewLine +
            "    <Assign DisplayName=\"Присваивание без аргументов\" />" + Environment.NewLine +
            "  </Sequence>" + Environment.NewLine +
            "</Activity>");

        try
        {
            DesignerBenchReport report = DesignerBenchProxy.Shared.Render(path);

            Assert.Multiple(() =>
            {
                Assert.That(report.LoadSucceeded, Is.True,
                    "XAML синтаксически корректен и обязан загрузиться." + Environment.NewLine + report.Description);
                Assert.That(report.ValidationErrors, Is.Not.Empty,
                    "Assign без To/Value обязан дать ошибку валидации: иначе проверки эталона ничего не проверяют." +
                    Environment.NewLine + report.Description);
                Assert.That(report.Ok, Is.False, Environment.NewLine + report.Description);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    // --- Стражи: среда дизайнера не должна разъезжаться по путям открытия. ---

    [Test]
    public void StudioLoadPath_UsesSharedDesignerEnvironment()
    {
        string helper = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "LoadActivityToDesignerHelper.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(helper, Does.Contain("WfDesignerEnvironment.Configure(workflowDesignerEx)"),
                "студия обязана настраивать дизайнер тем же конфигуратором, что и стенд: " +
                "иначе стенд проверяет не тот путь открытия");
            Assert.That(helper, Does.Not.Contain("DebugValidationErrorService"),
                "сервис ошибок валидации ставится внутри WfDesignerEnvironment — вторая точка настройки недопустима");
            Assert.That(helper, Does.Not.Contain("RegisterExpressionActivityEditor"),
                "регистрация Roslyn-редактора выражений — тоже часть общей среды дизайнера");
        });
    }

    [Test]
    public void DesignerEnvironment_KeepsMandatoryRequirements()
    {
        string source = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "WfDesignerEnvironment.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("TargetFrameworkName"), "целевую платформу дизайнера задаёт среда");
            Assert.That(source, Does.Contain("LoadingFromUntrustedSourceEnabled = true"),
                "без загрузки из недоверенного источника дизайнер отказывается открывать файлы проекта");
            Assert.That(source, Does.Contain("RegisterDesignerMetadata"),
                "метаданные стандартных активностей WF регистрирует среда, а не каждый вызывающий");
            Assert.That(source, Does.Contain("RegisterExpressionActivityEditor"),
                "без Roslyn-редактора выражения в конструкторе не редактируются");
            Assert.That(source, Does.Contain("IExpressionEditorService"), "редактор выражений в инспекторе свойств");
            Assert.That(source, Does.Contain("IValidationErrorService"),
                "без своего сервиса ошибок WF показывает модальное окно");
        });
    }

    // --- Вспомогательное. ---

    private static string WriteTempWorkflow(string xaml)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "ai-rpa-designer-probe-" + Guid.NewGuid().ToString("N") + ".xaml");

        File.WriteAllText(path, xaml, new UTF8Encoding(false));
        return path;
    }

    private static string ReferenceWorkflowPath()
    {
        return Path.Combine(FindRepositoryRoot(), "RPA-1", "Main.xaml");
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"не найден файл {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }
}
