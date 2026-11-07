using System.Globalization;
using System.Text;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Баг «при загрузке конструктора нет ошибки валидации поля, значок появляется только после клика по
/// полю; в окне «Свойства» — то же самое».
/// <para>
/// Проверяется на живом конструкторе и <b>без единого клика</b>: документ открывается, поле с невалидным
/// выражением обязано показать бейдж само — сразу или через непродолжительное время по фоновой проверке.
/// До исправления единственными триггерами были изменение текста и фокус
/// (<c>RoslynExpressionEditor.StartValidator</c> вызывался только из <c>OnGotTextBlockFocus</c> и
/// <c>OnGotEditingFocus</c>), поэтому бейдж появлялся ровно по клику.
/// </para>
/// <para>
/// Сценарий тяжёлый (живой <c>WorkflowDesigner</c>), поэтому выполняется один раз в
/// <see cref="OneTimeSetUpAttribute"/>: тесты утверждают по одному отчёту стенда.
/// </para>
/// </summary>
[TestFixture]
public class ExpressionFieldBadgeTests
{
    /// <summary>Сколько времени полю отпущено на «появилось само»: загрузка + фоновая проверка.</summary>
    private const double BadgeDeadlineMs = 10000;

    private static DesignerBenchFieldBadgeReport _report;
    private static string _xamlPath;

    [OneTimeSetUp]
    public void RunFieldBadgeScenario()
    {
        _xamlPath = ExpressionBadgeSamples.WriteInvalidExpressionXaml();
        _report = DesignerBenchProxy.Shared.ExpressionFieldBadgeScenario(
            _xamlPath,
            ExpressionBadgeSamples.BrokenExpression);
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

    /// <summary>Сценарий целиком выполнился: стенд жив, поле найдено, ошибок стенда нет.</summary>
    [Test]
    public void Scenario_CompletesOnLiveDesigner()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report, Is.Not.Null, "отчёт стенда не получен");
            Assert.That(_report.Errors, Is.Empty, Report());
            Assert.That(_report.Ok, Is.True, Report());
            Assert.That(_report.Stages, Is.Not.Empty, "замер не выполнен ни в одной точке" + Environment.NewLine + Report());
        });
    }

    /// <summary>
    /// Поле с невалидным выражением действительно найдено: без этого остальные утверждения были бы
    /// «зелёными по пустоте».
    /// </summary>
    [Test]
    public void BrokenField_IsRealizedInDesigner()
    {
        Assert.That(Fields(FirstStage()).Exists(field => string.Equals(
                field.Text.Trim(),
                ExpressionBadgeSamples.BrokenExpression,
                StringComparison.Ordinal)),
            Is.True,
            "в вьюхе Assign не найдено поле с выражением «"
            + ExpressionBadgeSamples.BrokenExpression + "»" + Environment.NewLine + Report());
    }

    /// <summary>
    /// Главный регресс карточки: бейдж в поле появляется <b>без клика</b> — по фоновой проверке документа,
    /// в пределах срока «непродолжительное время».
    /// </summary>
    [Test]
    public void BrokenField_ShowsBadgeWithoutClick()
    {
        FieldBadgeStage stage = Stage("покой");

        Assert.Multiple(() =>
        {
            Assert.That(stage, Is.Not.Null, "нет точки замера «покой»" + Environment.NewLine + Report());
            Assert.That(stage.ElapsedMs, Is.LessThan(BadgeDeadlineMs),
                "замер «покой» сделан позже срока появления бейджа" + Environment.NewLine + Report());
            Assert.That(BrokenField(stage), Is.Not.Null,
                "поле с выражением «" + ExpressionBadgeSamples.BrokenExpression + "» не найдено"
                + Environment.NewLine + Report());
            Assert.That(BrokenField(stage).HasBadge, Is.True,
                "бейдж ошибки не появился без клика по полю" + Environment.NewLine + Report());
        });
    }

    /// <summary>Пользователь видит именно значок: флага привязки мало, глиф обязан быть видимым.</summary>
    [Test]
    public void BrokenField_BadgeGlyphIsVisible()
    {
        Assert.That(BrokenField(Stage("покой"))?.BadgeVisibility, Is.EqualTo("Visible"),
            "глиф ошибки в поле не виден" + Environment.NewLine + Report());
    }

    /// <summary>Подсказка поля называет то же имя, что введено: иначе ошибку не связать с текстом.</summary>
    [Test]
    public void BrokenField_BadgeToolTipNamesTheExpression()
    {
        Assert.That(BrokenField(Stage("покой"))?.Message, Does.Contain(ExpressionBadgeSamples.BrokenExpression),
            "в подсказке бейджа нет имени невалидного выражения" + Environment.NewLine + Report());
    }

    /// <summary>
    /// Ошибка не выдумана и не переписана: тот же документ уже помечен иконкой студии на активности, и
    /// текст у поля — тот же, что у иконки, только без префикса имени аргумента. Два набора правил в
    /// одном месте дали бы разные слова про одну ошибку.
    /// </summary>
    [Test]
    public void BrokenField_BadgeMatchesActivityIconMessage()
    {
        string fieldMessage = BrokenField(Stage("покой"))?.Message ?? string.Empty;
        string iconMessage = string.Join(" ", _report.BrokenAddressMessages);

        Assert.Multiple(() =>
        {
            Assert.That(_report.BrokenAddressMessages, Is.Not.Empty,
                "у активности нет подсказки иконки с ошибкой выражения: фоновая проверка её не нашла"
                + Environment.NewLine + Report());
            Assert.That(fieldMessage, Is.Not.Empty, "у бейджа поля нет текста" + Environment.NewLine + Report());
            Assert.That(iconMessage, Does.Contain(fieldMessage),
                "текст бейджа поля и текст иконки активности разошлись" + Environment.NewLine + Report());
        });
    }

    /// <summary>
    /// Обратная сторона: корректное выражение (<c>WriteLine.Text</c> фикстуры, переменная
    /// <c>message</c>) бейджа не получает. Иначе «ошибка видна сразу» превращается в поток ложных ошибок.
    /// </summary>
    [Test]
    public void ValidField_HasNoBadge()
    {
        FieldBadgeEntry valid = Fields(Stage("покой"))
            .Find(field => string.Equals(
                field.Text.Trim(),
                ExpressionBadgeSamples.ValidExpression,
                StringComparison.Ordinal));

        Assert.That(valid, Is.Not.Null,
            "в фикстуре не найдено корректное поле «" + ExpressionBadgeSamples.ValidExpression + "»"
            + Environment.NewLine + Report());
        Assert.That(valid.HasBadge, Is.False,
            "корректное выражение помечено ошибкой" + Environment.NewLine + Report());
    }

    /// <summary>
    /// Поле в правке ведёт себя как раньше: после клика бейдж на месте (проверка редактора и вердикт
    /// документной проверки не спорят друг с другом).
    /// </summary>
    [Test]
    public void BrokenField_KeepsBadgeAfterClick()
    {
        Assert.That(BrokenField(Stage("после клика"))?.HasBadge, Is.True,
            "после клика бейдж пропал: два набора правил перебивают друг друга" + Environment.NewLine + Report());
    }

    /// <summary>
    /// Окно «Свойства»: поле аргумента показывает бейдж <b>без клика по полю</b>. WF строит поле
    /// инспектора, когда свойство показано, поэтому «до клика» здесь означает «сразу после постройки
    /// поля»: бейдж ставит вердикт документной проверки (<c>ExpressionFieldVerdicts</c>), а не
    /// собственная проверка редактора, которая раньше просыпалась только на фокус.
    /// </summary>
    [Test]
    public void Inspector_FieldShowsBadgeWithoutClick()
    {
        FieldBadgeStage stage = Stage("покой");
        FieldBadgeEntry broken = stage?.InspectorFields.Find(field => string.Equals(
            field.Text.Trim(),
            ExpressionBadgeSamples.BrokenExpression,
            StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(stage, Is.Not.Null, "нет точки замера «покой»" + Environment.NewLine + Report());
            Assert.That(stage.InspectorFields, Is.Not.Empty,
                "окно «Свойства» не построило ни одного поля выражения" + Environment.NewLine + Report());
            Assert.That(broken, Is.Not.Null,
                "в окне «Свойства» нет поля с выражением «" + ExpressionBadgeSamples.BrokenExpression + "»"
                + Environment.NewLine + Report());
            Assert.That(broken?.HasBadge, Is.True,
                "поле в окне «Свойства» без бейджа: ошибка снова видна только после клика"
                + Environment.NewLine + Report());
            Assert.That(broken?.BadgeVisibility, Is.EqualTo("Visible"),
                "значок ошибки в поле окна «Свойства» не виден" + Environment.NewLine + Report());
        });
    }

    // --- Разбор отчёта. ---

    private static FieldBadgeStage FirstStage()
    {
        return _report == null || _report.Stages.Count == 0 ? null : _report.Stages[0];
    }

    private static FieldBadgeStage Stage(string name)
    {
        return _report?.Stages.Find(stage => string.Equals(stage.Name, name, StringComparison.Ordinal));
    }

    private static List<FieldBadgeEntry> Fields(FieldBadgeStage stage)
    {
        return stage == null ? new List<FieldBadgeEntry>() : stage.Fields;
    }

    private static FieldBadgeEntry BrokenField(FieldBadgeStage stage)
    {
        return Fields(stage).Find(field => string.Equals(
            field.Text.Trim(),
            ExpressionBadgeSamples.BrokenExpression,
            StringComparison.Ordinal));
    }

    private static string Report()
    {
        if (_report == null)
        {
            return "отчёт стенда не получен";
        }

        return new StringBuilder(_report.Description)
            .AppendLine()
            .AppendLine("документ: " + _xamlPath)
            .AppendLine("окно «Свойства»: " + _report.InspectorState)
            .AppendLine("поля окна «Свойства»: " + string.Join(" | ", _report.InspectorDump))
            .ToString();
    }
}
