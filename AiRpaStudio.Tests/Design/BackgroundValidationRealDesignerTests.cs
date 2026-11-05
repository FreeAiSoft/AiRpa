using System.Globalization;
using System.Text;
using AiRpaStudio.Tests.Design;
using AiRpaStudio.Tests.Generation;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Шаги 0 и 5 плана <c>AiRpaStudio\docs\UI02_Background_Validation_Plan.md</c> на живом конструкторе:
/// нагрузочный документ (100+ активностей) открывается в настоящем <c>WorkflowDesigner</c>, проход
/// валидации выполняется в фоне, а замечания находят ошибки <b>внутри</b> тел и ветвей.
/// <para>
/// Почему стенд, а не юнит-тест: «иконка ошибки стоит на нужной активности» — факт о живом визуальном
/// дереве WF, синтетическая модель его не воспроизводит. Кроме того, только здесь видно, сколько на
/// самом деле длится каждая фаза прохода на большом документе (шаг 0).
/// </para>
/// <para>
/// Сценарий тяжёлый, поэтому выполняется один раз в <see cref="OneTimeSetUpAttribute"/>: тесты
/// утверждают по одному отчёту (числа и адреса), а не перезапускают конструктор.
/// </para>
/// </summary>
[TestFixture]
public class BackgroundValidationRealDesignerTests
{
    private static DesignerBenchValidationReport _report;
    private static string _xamlPath;
    private static string _loadProfile;

    [OneTimeSetUp]
    public void RunValidationPassScenario()
    {
        _xamlPath = ValidationWorkloadSamples.WriteWorkloadXaml(out _loadProfile);
        _report = DesignerBenchProxy.Shared.ValidationPassScenario(_xamlPath);
    }

    [OneTimeTearDown]
    public void RemoveTemporaryDirectory()
    {
        try
        {
            string directory = Path.GetDirectoryName(_xamlPath);
            if (directory != null && Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (Exception)
        {
            // Каталог мог остаться заблокированным конструктором — на результат прогона это не влияет.
        }
    }

    /// <summary>Сценарий целиком выполнился: стенд жив, проход дошёл до применения, ошибок стенда нет.</summary>
    [Test]
    public void Scenario_CompletesOnLiveDesigner()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report, Is.Not.Null, "отчёт стенда не получен");
            Assert.That(_report.Errors, Is.Empty, Report());
            Assert.That(_report.Ok, Is.True, Report());
            Assert.That(_report.DispatcherTicks, Is.GreaterThan(0),
                "пробник диспетчера не тикал: замер отзывчивости не состоялся" + Environment.NewLine + Report());
        });
    }

    /// <summary>Документ действительно нагрузочный: 100+ активностей в области валидации.</summary>
    [Test]
    public void WorkloadDocument_IsLargeEnoughForBaseline()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.ScopeActivityCount, Is.GreaterThan(100),
                "документ меньше 100 активностей — замер шага 0 не о нагрузочном случае" + Environment.NewLine + Report());
            Assert.That(_report.CheckCount, Is.GreaterThan(100),
                "проверок выражений меньше, чем активностей: часть документа не обойдена" + Environment.NewLine + Report());
        });
    }

    /// <summary>
    /// Главный регресс карточки: ошибки видны <b>сразу после загрузки</b>, без единой правки документа
    /// (раньше единственный «инициализационный» проход отрабатывал до построения поверхности).
    /// </summary>
    [Test]
    public void Pass_RunsWithoutAnyEdit()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.FirstPassSeen, Is.True, "проход валидации не выполнился сам" + Environment.NewLine + Report());
            Assert.That(_report.PassState, Does.Contain("выполнено"), Report());
            Assert.That(_report.ErrorCount, Is.GreaterThan(0),
                "в нагрузочном документе есть ошибки выражений — фон обязан их найти" + Environment.NewLine + Report());
        });
    }

    /// <summary>
    /// «Валидация кастомных активностей»: тело <c>ForEach</c> — это <c>ActivityAction</c>, не активность.
    /// Прежний обход обрывался на нём (<c>else { break; }</c>) и до ошибки не доходил.
    /// </summary>
    [Test]
    public void ErrorsFound_InsideForEachBody()
    {
        Assert.That(_report.ErrorAddresses, Does.Contain(ValidationWorkloadSamples.ForEachErrorAddress),
            "ошибка выражения в теле ForEach (ActivityAction) не найдена" + Environment.NewLine + Report());
    }

    /// <summary>Ветка <c>Pick</c> — <c>PickBranch</c>, второй тип контейнера, который прежний обход терял.</summary>
    [Test]
    public void ErrorsFound_InsidePickBranch()
    {
        Assert.That(_report.ErrorAddresses, Does.Contain(ValidationWorkloadSamples.PickErrorAddress),
            "ошибка выражения в действии ветки Pick (PickBranch) не найдена" + Environment.NewLine + Report());
    }

    /// <summary>Вложенная <c>Sequence</c> в <c>Then</c> — третья форма вложенности.</summary>
    [Test]
    public void ErrorsFound_InsideThen()
    {
        Assert.That(_report.ErrorAddresses, Does.Contain(ValidationWorkloadSamples.IfErrorAddress),
            "ошибка выражения внутри Then не найдена" + Environment.NewLine + Report());
    }

    /// <summary>
    /// Обратная сторона обхода: корректное тело цикла (<c>item</c> — аргумент делегата) не должно быть
    /// помечено. Иначе «валидация кастомных активностей» превращается в поток ложных ошибок.
    /// </summary>
    [Test]
    public void ValidActivityInsideForEachBody_HasNoErrors()
    {
        Assert.That(_report.ErrorAddresses, Does.Not.Contain(ValidationWorkloadSamples.ValidForEachAddress),
            "корректное тело ForEach помечено ошибкой" + Environment.NewLine + Report());
    }

    /// <summary>
    /// Иконка на вложенной активности — своя, студии: в подсказке виден текст ошибки компиляции выражения.
    /// Проверять одну видимость <c>Border</c> нельзя: у WF есть такие же иконки по своему состоянию.
    /// </summary>
    [Test]
    public void NestedIconInsideForEachBody_ShowsOwnExpressionError()
    {
        Assert.That(_report.AddressToolTips, Does.ContainKey(ValidationWorkloadSamples.ForEachErrorAddress),
            "у активности в теле ForEach нет иконки с подсказкой студии" + Environment.NewLine + Report());

        Assert.That(_report.AddressToolTips[ValidationWorkloadSamples.ForEachErrorAddress],
            Does.Contain(ValidationWorkloadSamples.BrokenExpression),
            "подсказка иконки не содержит текст ошибки выражения" + Environment.NewLine + Report());
    }

    /// <summary>У корректного тела цикла своей ошибки в подсказке нет.</summary>
    [Test]
    public void ValidActivityInsideForEachBody_HasNoOwnErrorMessage()
    {
        if (_report.AddressToolTips.TryGetValue(ValidationWorkloadSamples.ValidForEachAddress, out string toolTip))
        {
            Assert.That(toolTip, Does.Not.Contain(ValidationWorkloadSamples.BrokenExpression),
                "в подсказке корректной активности оказалось чужое сообщение об ошибке" + Environment.NewLine + Report());
        }
    }

    /// <summary>
    /// Шаг 0: «было/стало» для UI-потока. Компиляция выражений раньше шла на UI-потоке, теперь — в фоне;
    /// <see cref="DesignerBenchValidationReport.OldUiMs"/> складывает то, что было бы на UI-потоке, с тем,
    /// что на нём осталось. Оценка снизу: двойную компиляцию литералов она не учитывает.
    /// </summary>
    [Test]
    public void UiThreadTime_IsLowerThanBeforeBackgroundPass()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.ExpressionsMs, Is.GreaterThan(0),
                "фоновая фаза не измерена: без неё сравнение «до/после» ничего не значит" + Environment.NewLine + Report());
            Assert.That(_report.UiMs, Is.LessThan(_report.OldUiMs),
                "UI-поток не разгрузился: фоновая компиляция не дала выигрыша" + Environment.NewLine + Report());
            Assert.That(_report.UiMs, Is.LessThan(_report.ExpressionsMs),
                "UI-поток занят не меньше, чем фоновая компиляция — перенос не имеет смысла" + Environment.NewLine + Report());
        });
    }

    /// <summary>
    /// Шаг 0: конструктор остаётся отзывчивым во время прохода. Граница намеренно грубая (полсекунды):
    /// точная цифра — в отчёте прогона, а тест ловит регресс «UI снова встал на весь проход».
    /// </summary>
    [Test]
    public void DispatcherStaysResponsiveDuringPass()
    {
        Assert.That(_report.MaxDispatcherGapMs, Is.LessThan(500.0),
            "UI-поток вставал на " + _report.MaxDispatcherGapMs.ToString("0.0", CultureInfo.InvariantCulture)
            + " мс за один проход валидации" + Environment.NewLine + Report());
    }

    /// <summary>Правка документа запускает новый проход, и его результат относится уже к новому поколению.</summary>
    [Test]
    public void ModelEdit_StartsNewPassOfNewGeneration()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.ModelEdited, Is.True, "правку модели на стенде выполнить не удалось" + Environment.NewLine + Report());
            Assert.That(_report.EditStartedNewPass, Is.True,
                "после правки проход не запустился (или результат остался от старого поколения)" + Environment.NewLine + Report());
            Assert.That(_report.GenerationAfterEdit, Is.GreaterThan(_report.Generation),
                "поколение не выросло после правки" + Environment.NewLine + Report());
        });
    }

    /// <summary>Закрытие вкладки: валидатор освобождён, тиков больше нет.</summary>
    [Test]
    public void DetachValidation_StopsTicks()
    {
        Assert.That(_report.PassesAfterDetach, Is.EqualTo(0),
            "после закрытия вкладки продолжились проходы валидации" + Environment.NewLine + Report());
    }

    /// <summary>Отчёт прогона: цифры прохода и адреса (журнал шага 0).</summary>
    private static string Report()
    {
        if (_report == null)
        {
            return "отчёт стенда не получен";
        }

        var text = new StringBuilder(_report.Description);
        text.AppendLine();
        text.AppendLine("область дизайнера: " + _loadProfile);
        text.AppendLine("активностей в области валидации: "
                        + _report.ScopeActivityCount.ToString(CultureInfo.InvariantCulture)
                        + ", по WorkflowInspectionServices: "
                        + _report.InspectionActivityCount.ToString(CultureInfo.InvariantCulture));
        text.AppendLine("адреса замечаний: " + Short(_report.ErrorAddresses));
        text.AppendLine("подсказки иконок: " + Short(_report.AddressToolTips.Keys));
        text.AppendLine("без вьюхи: " + _report.NoViewAddresses.Count.ToString(CultureInfo.InvariantCulture)
                        + " адрес(ов), напр. " + Short(_report.NoViewAddresses));

        return text.ToString();
    }

    /// <summary>Список для сообщения: первые 8 элементов, иначе сообщение становится нечитаемым.</summary>
    private static string Short(IEnumerable<string> values)
    {
        if (values == null)
        {
            return "(нет)";
        }

        var list = new List<string>(values);
        if (list.Count == 0)
        {
            return "(нет)";
        }

        string head = string.Join(" | ", list.Take(8));
        return list.Count > 8 ? head + " … (всего " + list.Count.ToString(CultureInfo.InvariantCulture) + ")" : head;
    }
}
